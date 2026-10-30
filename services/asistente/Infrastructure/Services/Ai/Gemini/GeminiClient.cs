using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common.Ai;
using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Ai.Gemini;

public sealed class GeminiClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    IAiAuditService aiAuditService,
    ILogger<GeminiClient> logger,
    IExerciseCatalogService? catalogService = null) : IAiClient
{
    private readonly GeminiOptions _options = options.Value;

    public string ProviderName => "Gemini";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var prompt = RoutinePromptBuilder.BuildPrompt(request, request.AvailableExercises);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            stopwatch.Stop();
            logger?.LogWarning("API Key de Gemini no configurada. Omitiendo llamada a Gemini y pasando al respaldo.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "SinApiKey",
                athleteId: request.AthleteId,
                errorMessage: "API Key de Gemini no configurada."));
            return null;
        }

        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0.2
                }
            };

            var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://generativelanguage.googleapis.com" : _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1beta/models/{_options.Model}:generateContent";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                requestMessage.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await httpClient.SendAsync(requestMessage, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                stopwatch.Stop();
                logger?.LogWarning("Se agotó la cuota de solicitudes de la capa gratuita de Gemini (HTTP 429). Pasando al siguiente respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "CuotaAgotada",
                    athleteId: request.AthleteId,
                    errorMessage: "Cuota excedida de la capa gratuita de Gemini (HTTP 429)."));
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger?.LogWarning("Gemini respondió con error {StatusCode}. Activando respaldo.", response.StatusCode);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorHttp",
                    athleteId: request.AthleteId,
                    errorMessage: $"Gemini HTTP {(int)response.StatusCode} ({response.StatusCode})"));
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            return await ParseAndLogGeminiResponseAsync(request.AthleteId, prompt, responseBody, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger?.LogWarning("La llamada a Gemini superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
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
            logger?.LogWarning(ex, "Gemini no está disponible ({Message}). Activando respaldo.", ex.Message);
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

    public async Task<PlanDraftContentDto?> TryGeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<CatalogExerciseDto> catalog = [];
        if (catalogService != null)
        {
            catalog = await catalogService.GetAllAsync(cancellationToken);
        }

        // KAN-563: Si el catálogo está vacío, no inventar IDs de respaldo y pasar directo al siguiente proveedor
        if (catalog == null || catalog.Count == 0)
        {
            logger?.LogWarning("Catálogo de ejercicios vacío. Omitiendo Gemini y pasando al siguiente proveedor.");
            return null;
        }

        // KAN-563: Filtrar ejercicios según el objetivo (priorizando grupos musculares solicitados)
        var availableExercises = RoutinePromptBuilder.FilterExercisesForGoal(catalog, request.Goal, maxExercises: 25);
        if (availableExercises.Count == 0)
        {
            availableExercises = catalog.Take(25).ToList();
        }

        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, availableExercises, matchDates);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            stopwatch.Stop();
            logger?.LogWarning("API Key de Gemini no configurada. Omitiendo llamada a Gemini y pasando al respaldo.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "SinApiKey",
                athleteId: request.AthleteId,
                errorMessage: "API Key de Gemini no configurada."));
            return null;
        }

        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    temperature = 0.2
                }
            };

            var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://generativelanguage.googleapis.com" : _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1beta/models/{_options.Model}:generateContent";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                requestMessage.Headers.TryAddWithoutValidation("x-goog-api-key", _options.ApiKey);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await httpClient.SendAsync(requestMessage, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                stopwatch.Stop();
                logger?.LogWarning("Se agotó la cuota de solicitudes de Gemini (HTTP 429). Pasando al siguiente respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "CuotaAgotada",
                    athleteId: request.AthleteId,
                    errorMessage: "Cuota excedida de Gemini (HTTP 429)."));
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger?.LogWarning("Gemini respondió con error {StatusCode}. Activando respaldo.", response.StatusCode);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorHttp",
                    athleteId: request.AthleteId,
                    errorMessage: $"Gemini HTTP {(int)response.StatusCode} ({response.StatusCode})"));
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();

            return await ParseAndLogGeminiPlanDraftResponseAsync(request.AthleteId, prompt, responseBody, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger?.LogWarning("La llamada a Gemini superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
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
            logger?.LogWarning(ex, "Gemini no está disponible ({Message}). Activando respaldo.", ex.Message);
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

    private async Task<RoutineSuggestionResponseDto?> ParseAndLogGeminiResponseAsync(
        Guid? athleteId,
        string prompt,
        string jsonResponse,
        long latencyMs)
    {
        using var document = JsonDocument.Parse(jsonResponse);
        var root = document.RootElement;

        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;

        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var ct)) completionTokens = ct.GetInt32();
            if (usage.TryGetProperty("totalTokenCount", out var tt)) totalTokens = tt.GetInt32();
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            logger?.LogWarning("La respuesta de Gemini no contuvo candidatos válidos.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: "SinCandidatos",
                response: jsonResponse,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: "La respuesta no contuvo candidatos válidos."));
            return null;
        }

        var text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            logger?.LogWarning("El contenido generado por Gemini vino vacío.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: "ContenidoVacio",
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: "El contenido generado vino vacío."));
            return null;
        }

        var validationResult = AiRoutineParser.ParseAndValidate(text);
        if (!validationResult.IsValid)
        {
            var validationTag = validationResult.IsSyntaxError ? "JsonInvalido" : "SchemaInvalido";
            logger?.LogWarning("La propuesta de rutina generada por Gemini no cumple con el esquema estricto o rangos válidos ({ValidationTag}): {Error}. Activando respaldo.",
                validationTag, validationResult.ErrorMessage);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: validationTag,
                response: text,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: validationResult.ErrorMessage));
            return null;
        }

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            latencyMs,
            isValid: true,
            validationResult: "Valido",
            response: text,
            athleteId: athleteId,
            promptTokens: promptTokens,
            completionTokens: completionTokens,
            totalTokens: totalTokens));

        return new RoutineSuggestionResponseDto(
            AthleteId: athleteId ?? Guid.Empty,
            ProposedTitle: validationResult.ProposedTitle ?? "Rutina Asistida por IA",
            JustificationSummary: validationResult.JustificationSummary ?? "Propuesta generada mediante Google Gemini.",
            Exercises: validationResult.Exercises.ToList(),
            ProviderUsed: ProviderName
        );
    }

    private async Task<PlanDraftContentDto?> ParseAndLogGeminiPlanDraftResponseAsync(
        Guid? athleteId,
        string prompt,
        string jsonResponse,
        long latencyMs)
    {
        using var document = JsonDocument.Parse(jsonResponse);
        var root = document.RootElement;

        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;

        if (root.TryGetProperty("usageMetadata", out var usage))
        {
            if (usage.TryGetProperty("promptTokenCount", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("candidatesTokenCount", out var ct)) completionTokens = ct.GetInt32();
            if (usage.TryGetProperty("totalTokenCount", out var tt)) totalTokens = tt.GetInt32();
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            logger?.LogWarning("La respuesta de Gemini no contuvo candidatos válidos.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: "SinCandidatos",
                response: jsonResponse,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: "La respuesta no contuvo candidatos válidos."));
            return null;
        }

        var text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            logger?.LogWarning("El contenido generado por Gemini vino vacío.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: "ContenidoVacio",
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: "El contenido generado vino vacío."));
            return null;
        }

        var (isValid, content, errorMessage, isSyntaxError) = PlanDraftAiParser.Parse(text);
        if (!isValid)
        {
            var validationTag = isSyntaxError ? "JsonInvalido" : "SchemaInvalido";
            logger?.LogWarning("La propuesta de planificación de Gemini no cumple con el esquema ({ValidationTag}): {Error}. Activando respaldo.",
                validationTag, errorMessage);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: validationTag,
                response: text,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: errorMessage));
            return null;
        }

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            latencyMs,
            isValid: true,
            validationResult: "Valido",
            response: text,
            athleteId: athleteId,
            promptTokens: promptTokens,
            completionTokens: completionTokens,
            totalTokens: totalTokens));

        return content;
    }
}
