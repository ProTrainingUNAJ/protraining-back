using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Ai.Gemini;

public sealed class GeminiClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    IAiAuditService aiAuditService,
    ILogger<GeminiClient> logger) : IAiClient
{
    private readonly GeminiOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string ProviderName => "Gemini";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var prompt = BuildAnonymousPrompt(request);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            stopwatch.Stop();
            logger.LogWarning("API Key de Gemini no configurada. Omitiendo llamada a Gemini y pasando al respaldo.");
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

            var response = await httpClient.SendAsync(requestMessage, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                stopwatch.Stop();
                logger.LogWarning("Se agotó la cuota de solicitudes de la capa gratuita de Gemini (HTTP 429). Pasando al siguiente respaldo.");
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
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                logger.LogWarning("Gemini respondió con error {StatusCode}: {ErrorBody}. Activando respaldo.", response.StatusCode, errorBody);
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "ErrorHttp",
                    athleteId: request.AthleteId,
                    errorMessage: $"Gemini HTTP {(int)response.StatusCode} ({response.StatusCode}): {errorBody}"));
                return null;
            }

            var responseString = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            stopwatch.Stop();
            return await ParseAndLogGeminiResponseAsync(request.AthleteId, prompt, responseString, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogWarning("La llamada a Gemini superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
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
            logger.LogWarning(ex, "Error al comunicarse con la API de Gemini: {Message}. Activando respaldo.", ex.Message);
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: "ErrorGeneral",
                athleteId: request.AthleteId,
                errorMessage: ex.Message));
            return null;
        }
    }

    private static string BuildAnonymousPrompt(RoutineSuggestionRequestDto request)
    {
        var muscles = request.MuscleGroupEmphasis is { Count: > 0 }
            ? string.Join(", ", request.MuscleGroupEmphasis)
            : "General";

        return $$"""
            Eres un asistente de entrenamiento para preparadores físicos.
            Diseña una propuesta de rutina para el atleta con identificador anónimo '{{request.AthleteId}}'.
            Objetivo: {{request.Goal}}
            Énfasis muscular: {{muscles}}

            REGLA ESTRICTA DE PRIVACIDAD: No incluyas nombres de personas.
            Responde ÚNICAMENTE con un JSON que tenga esta estructura exacta:
            {
              "tituloPropuesto": "Título de la rutina",
              "resumenJustificacion": "Explicación breve del criterio y adaptaciones aplicadas",
              "ejercicios": [
                {
                  "ejercicioNombre": "Nombre del ejercicio",
                  "series": 3,
                  "repeticiones": 10,
                  "porcentajeSugerido": 70.0
                }
              ]
            }
            """;
    }

    private async Task<RoutineSuggestionResponseDto?> ParseAndLogGeminiResponseAsync(
        Guid athleteId,
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
            logger.LogWarning("La respuesta de Gemini no contuvo candidatos válidos.");
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
            logger.LogWarning("El contenido generado por Gemini vino vacío.");
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

        var parsed = JsonSerializer.Deserialize<RawRoutineProposal>(text, JsonOptions);
        if (parsed == null || parsed.Exercises == null || parsed.Exercises.Count == 0)
        {
            logger.LogWarning("No se pudo deserializar la rutina generada por Gemini.");
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                latencyMs,
                isValid: false,
                validationResult: "JsonInvalido",
                response: text,
                athleteId: athleteId,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                totalTokens: totalTokens,
                errorMessage: "No se pudo deserializar la rutina generada por Gemini."));
            return null;
        }

        var exercises = parsed.Exercises.Select(e => new SuggestedExerciseDto(
            ExerciseName: e.ExerciseName ?? "Ejercicio general",
            Sets: e.Sets > 0 ? e.Sets : 3,
            Reps: e.Reps > 0 ? e.Reps : 10,
            SuggestedPercentage: e.SuggestedPercentage > 0 ? e.SuggestedPercentage : 65m
        )).ToList();

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
            AthleteId: athleteId,
            ProposedTitle: parsed.ProposedTitle ?? "Rutina Asistida por IA",
            JustificationSummary: parsed.JustificationSummary ?? "Propuesta generada mediante Google Gemini.",
            Exercises: exercises,
            ProviderUsed: ProviderName
        );
    }

    private sealed class RawRoutineProposal
    {
        [JsonPropertyName("tituloPropuesto")]
        public string? ProposedTitle { get; set; }

        [JsonPropertyName("resumenJustificacion")]
        public string? JustificationSummary { get; set; }

        [JsonPropertyName("ejercicios")]
        public List<RawExercise>? Exercises { get; set; }
    }

    private sealed class RawExercise
    {
        [JsonPropertyName("ejercicioNombre")]
        public string? ExerciseName { get; set; }

        [JsonPropertyName("series")]
        public int Sets { get; set; }

        [JsonPropertyName("repeticiones")]
        public int Reps { get; set; }

        [JsonPropertyName("porcentajeSugerido")]
        public decimal SuggestedPercentage { get; set; }
    }
}
