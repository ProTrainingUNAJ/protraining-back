using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Application.Common.Ai;
using Application.Common.DayAdjustments;
using Application.Common.PlanDrafts;
using Application.Common.Recommendations;
using Application.DTOs.Ai;
using Application.DTOs.DayAdjustments;
using Application.DTOs.PlanDrafts;
using Application.DTOs.Recommendations;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Ai.Ollama;

public sealed class OllamaClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    IAiAuditService aiAuditService,
    ILogger<OllamaClient> logger,
    IExerciseCatalogService? catalogService = null) : IAiClient
{
    private readonly OllamaOptions _options = options.Value;

    public string ProviderName => "Ollama";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var prompt = RoutinePromptBuilder.BuildPrompt(request, request.AvailableExercises);

        try
        {
            var payload = new
            {
                model = _options.Model,
                prompt,
                format = "json",
                stream = false
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/generate";
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await httpClient.PostAsync(url, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger?.LogWarning("Ollama respondió con error {StatusCode}. Activando respaldo.", response.StatusCode);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorHttp",
                    athleteId: request.AthleteId,
                    errorMessage: $"Ollama HTTP {(int)response.StatusCode} ({response.StatusCode})"));
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var document = JsonDocument.Parse(responseBody);

            int? promptTokens = document.RootElement.TryGetProperty("prompt_eval_count", out var pProp) ? pProp.GetInt32() : null;
            int? completionTokens = document.RootElement.TryGetProperty("eval_count", out var eProp) ? eProp.GetInt32() : null;

            if (!document.RootElement.TryGetProperty("response", out var rawTextProp))
            {
                stopwatch.Stop();
                logger?.LogWarning("La respuesta de Ollama no contuvo la propiedad 'response'. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "FormatoInvalido",
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "La respuesta no contuvo la propiedad 'response'."));
                return null;
            }

            var rawText = rawTextProp.GetString();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                stopwatch.Stop();
                logger?.LogWarning("El modelo de Ollama devolvió una respuesta vacía. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "RespuestaVacia",
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "El modelo devolvió texto vacío."));
                return null;
            }

            var validationResult = AiRoutineParser.ParseAndValidate(rawText);
            if (!validationResult.IsValid)
            {
                stopwatch.Stop();
                var validationTag = validationResult.IsSyntaxError ? "JsonInvalido" : "SchemaInvalido";
                logger?.LogWarning("La respuesta de Ollama no cumple con el esquema JSON o rangos válidos ({ValidationTag}): {Errors}. Activando respaldo.", validationTag, validationResult.ErrorMessage);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: validationTag,
                    response: rawText,
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: validationResult.ErrorMessage));
                return null;
            }

            stopwatch.Stop();
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: true,
                validationResult: "Valido",
                response: rawText,
                athleteId: request.AthleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens));

            return new RoutineSuggestionResponseDto(
                AthleteId: request.AthleteId,
                ProposedTitle: validationResult.ProposedTitle ?? "Rutina Generada por Ollama",
                JustificationSummary: validationResult.JustificationSummary ?? "Propuesta generada localmente por Llama.",
                Exercises: validationResult.Exercises.ToList(),
                ProviderUsed: ProviderName
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger?.LogWarning("La llamada a Ollama superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "Timeout",
                athleteId: request.AthleteId,
                errorMessage: $"Superó el tiempo límite de {_options.TimeoutSeconds} segundos."));
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogWarning(ex, "Ollama no está disponible ({Message}). Activando respaldo.", ex.Message);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "ErrorConexion",
                athleteId: request.AthleteId,
                errorMessage: ex.Message));
            return null;
        }
    }

    public async Task<string?> TryGenerateDayAdjustmentJustificationAsync(
        IReadOnlyList<DayAdjustmentItemDto> items,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (items.Count == 0)
            return null;

        var stopwatch = Stopwatch.StartNew();
        var prompt = DayAdjustmentMapper.BuildJustificationPrompt(items);

        try
        {
            var payload = new
            {
                model = _options.Model,
                prompt,
                format = "json",
                stream = false
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/generate";
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await httpClient.PostAsync(url, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger?.LogWarning("Ollama respondió con error {StatusCode} al redactar justificación de ajuste del día. Activando respaldo.", response.StatusCode);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var document = JsonDocument.Parse(responseBody);

            if (!document.RootElement.TryGetProperty("response", out var rawTextProp))
            {
                stopwatch.Stop();
                return null;
            }

            var justification = DayAdjustmentMapper.ExtractJustificationFromModelOutput(rawTextProp.GetString());
            stopwatch.Stop();
            return string.IsNullOrWhiteSpace(justification) ? null : justification;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogWarning(ex, "Ollama no estuvo disponible para redactar la justificación del ajuste del día. Activando respaldo.");
            return null;
        }
    }

    public async Task<PlanDraftContentDto?> TryGeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<CatalogExerciseDto> availableExercises = [];
        if (catalogService != null)
        {
            availableExercises = await catalogService.GetExercisesForGoalAsync(request.Goal, 25, cancellationToken);
        }

        // KAN-563: Si el catálogo está vacío, no inventar IDs de respaldo y pasar directo al siguiente proveedor
        if (availableExercises == null || availableExercises.Count == 0)
        {
            logger?.LogWarning("Catálogo de ejercicios vacío. Omitiendo Ollama y pasando al siguiente proveedor.");
            return null;
        }

        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, availableExercises, matchDates);

        try
        {
            var payload = new
            {
                model = _options.Model,
                prompt,
                format = "json",
                stream = false
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/generate";
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await httpClient.PostAsync(url, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger?.LogWarning("Ollama respondió con error {StatusCode}. Activando respaldo.", response.StatusCode);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorHttp",
                    athleteId: request.AthleteId,
                    errorMessage: $"Ollama HTTP {(int)response.StatusCode} ({response.StatusCode})"));
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var document = JsonDocument.Parse(responseBody);

            int? promptTokens = document.RootElement.TryGetProperty("prompt_eval_count", out var pProp) ? pProp.GetInt32() : null;
            int? completionTokens = document.RootElement.TryGetProperty("eval_count", out var eProp) ? eProp.GetInt32() : null;

            if (!document.RootElement.TryGetProperty("response", out var rawTextProp))
            {
                stopwatch.Stop();
                logger?.LogWarning("La respuesta de Ollama no contuvo la propiedad 'response'. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "FormatoInvalido",
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "La respuesta no contuvo la propiedad 'response'."));
                return null;
            }

            var rawText = rawTextProp.GetString();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                stopwatch.Stop();
                logger?.LogWarning("El modelo de Ollama devolvió una respuesta vacía. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "RespuestaVacia",
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "El modelo devolvió texto vacío."));
                return null;
            }

            var (isValid, parsedContent, errorMessage, isSyntaxError) = PlanDraftAiParser.Parse(rawText);
            if (!isValid)
            {
                stopwatch.Stop();
                var validationTag = isSyntaxError ? "JsonInvalido" : "SchemaInvalido";
                logger?.LogWarning("La respuesta de Ollama no cumple con el esquema del borrador ({ValidationTag}): {Errors}. Activando respaldo.", validationTag, errorMessage);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: validationTag,
                    response: rawText,
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: errorMessage));
                return null;
            }

            stopwatch.Stop();
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: true,
                validationResult: "Valido",
                response: rawText,
                athleteId: request.AthleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens));

            return parsedContent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger?.LogWarning("La llamada a Ollama superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "Timeout",
                athleteId: request.AthleteId,
                errorMessage: $"Superó el tiempo límite de {_options.TimeoutSeconds} segundos."));
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogWarning(ex, "Ollama no está disponible ({Message}). Activando respaldo.", ex.Message);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "ErrorConexion",
                athleteId: request.AthleteId,
                errorMessage: ex.Message));
            return null;
        }
    }

    public Task<TrainingRecommendationResponseDto?> TryGenerateTrainingRecommendationAsync(
        TrainingRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prompt = RecommendationPromptBuilder.BuildTrainingRecommendationPrompt(request);

        return ExecuteRecommendationPromptAsync(
            prompt,
            request.AthleteId,
            rawText => RecommendationOutputValidator.ValidateTrainingOutput(rawText, request),
            dto => dto with { ProviderUsed = ProviderName },
            cancellationToken);
    }

    public Task<NutritionRecommendationResponseDto?> TryGenerateNutritionRecommendationAsync(
        NutritionRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);

        return ExecuteRecommendationPromptAsync(
            prompt,
            request.AthleteId,
            rawText => RecommendationOutputValidator.ValidateNutritionOutput(rawText, request),
            dto => dto with { ProviderUsed = ProviderName },
            cancellationToken);
    }

    private async Task<T?> ExecuteRecommendationPromptAsync<T>(
        string prompt,
        Guid? athleteId,
        Func<string, RecommendationValidationResult<T>> validator,
        Func<T, T> attachProvider,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var payload = new
            {
                model = _options.Model,
                prompt,
                format = "json",
                stream = false
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/generate";
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await httpClient.PostAsync(url, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger?.LogWarning("Ollama respondió con error {StatusCode} al generar recomendación. Activando respaldo.", response.StatusCode);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorHttp",
                    athleteId: athleteId,
                    errorMessage: $"Ollama HTTP {(int)response.StatusCode} ({response.StatusCode})"), cancellationToken);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            using var document = JsonDocument.Parse(responseBody);

            int? promptTokens = document.RootElement.TryGetProperty("prompt_eval_count", out var pProp) ? pProp.GetInt32() : null;
            int? completionTokens = document.RootElement.TryGetProperty("eval_count", out var eProp) ? eProp.GetInt32() : null;

            if (!document.RootElement.TryGetProperty("response", out var rawTextProp))
            {
                stopwatch.Stop();
                logger?.LogWarning("La respuesta de Ollama no contuvo la propiedad 'response'. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "FormatoInvalido",
                    athleteId: athleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "La respuesta no contuvo la propiedad 'response'."), cancellationToken);
                return null;
            }

            var rawText = rawTextProp.GetString();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                stopwatch.Stop();
                logger?.LogWarning("El modelo de Ollama devolvió una respuesta vacía al generar recomendación. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "RespuestaVacia",
                    athleteId: athleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "El modelo devolvió texto vacío."), cancellationToken);
                return null;
            }

            var validationResult = validator(rawText);
            if (!validationResult.IsValid || validationResult.Value is null)
            {
                stopwatch.Stop();
                logger?.LogWarning(
                    "La recomendación de Ollama fue rechazada por el validador ({ValidationCode}): {Error}. Activando respaldo.",
                    validationResult.ValidationCode,
                    validationResult.ErrorMessage);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: validationResult.ValidationCode,
                    response: rawText,
                    athleteId: athleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: validationResult.ErrorMessage), cancellationToken);
                return null;
            }

            stopwatch.Stop();
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: true,
                validationResult: "Valido",
                response: rawText,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens), cancellationToken);

            return attachProvider(validationResult.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger?.LogWarning("La llamada a Ollama superó el tiempo límite de {Seconds} segundos al generar recomendación. Activando respaldo.", _options.TimeoutSeconds);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "Timeout",
                athleteId: athleteId,
                errorMessage: $"Superó el tiempo límite de {_options.TimeoutSeconds} segundos."), cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger?.LogWarning(ex, "Ollama no estuvo disponible al generar recomendación ({Message}). Activando respaldo.", ex.Message);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "ErrorConexion",
                athleteId: athleteId,
                errorMessage: ex.Message), cancellationToken);
            return null;
        }
    }
}
