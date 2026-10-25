using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Ai.Ollama;

public sealed class OllamaClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    IAiAuditService aiAuditService,
    ILogger<OllamaClient> logger) : IAiClient
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

            var response = await httpClient.PostAsync(url, content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                stopwatch.Stop();
                logger.LogWarning("Ollama respondió con error {StatusCode}. Activando respaldo.", response.StatusCode);
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
                logger.LogWarning("La respuesta de Ollama no contuvo la propiedad 'response'. Activando respaldo.");
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
                logger.LogWarning("El modelo de Ollama devolvió una respuesta vacía. Activando respaldo.");
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
                logger.LogWarning("La respuesta de Ollama no cumple con el esquema JSON o rangos válidos ({ValidationTag}): {Errors}. Activando respaldo.", validationTag, validationResult.ErrorMessage);
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
            logger.LogWarning("La llamada a Ollama superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
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
            logger.LogWarning(ex, "Ollama no está disponible ({Message}). Activando respaldo.", ex.Message);
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
}
