using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string ProviderName => "Ollama";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var prompt = BuildAnonymousPrompt(request);

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
                    errorMessage: $"Ollama HTTP {response.StatusCode}"));
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

            var parsed = JsonSerializer.Deserialize<RawRoutineProposal>(rawText, JsonOptions);
            if (parsed == null || parsed.Exercises == null || parsed.Exercises.Count == 0)
            {
                stopwatch.Stop();
                logger.LogWarning("No se pudo deserializar la rutina generada por Ollama. Activando respaldo.");
                await aiAuditService.LogAsync(AiLog.Create(
                    ProviderName,
                    prompt,
                    stopwatch.ElapsedMilliseconds,
                    isValid: false,
                    validationResult: "JsonInvalido",
                    response: rawText,
                    athleteId: request.AthleteId,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    errorMessage: "El JSON generado no coincide con la estructura de ejercicios."));
                return null;
            }

            stopwatch.Stop();
            var exercises = parsed.Exercises.Select(e => new SuggestedExerciseDto(
                ExerciseName: e.ExerciseName ?? "Ejercicio general",
                Sets: e.Sets > 0 ? e.Sets : 3,
                Reps: e.Reps > 0 ? e.Reps : 10,
                SuggestedPercentage: e.SuggestedPercentage > 0 ? e.SuggestedPercentage : 65m
            )).ToList();

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
                ProposedTitle: parsed.ProposedTitle ?? "Rutina Generada por Ollama",
                JustificationSummary: parsed.JustificationSummary ?? "Propuesta generada localmente por Llama.",
                Exercises: exercises,
                ProviderUsed: ProviderName
            );
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

    private static string BuildAnonymousPrompt(RoutineSuggestionRequestDto request)
    {
        var muscles = request.MuscleGroupEmphasis is { Count: > 0 }
            ? string.Join(", ", request.MuscleGroupEmphasis)
            : "General";

        return $$"""
            Eres un asistente para preparadores físicos.
            Diseña una propuesta de rutina para el atleta '{{request.AthleteId}}'.
            Objetivo: {{request.Goal}}
            Énfasis muscular: {{muscles}}

            Responde únicamente con un JSON con esta estructura exacta:
            {
              "tituloPropuesto": "Título de la rutina",
              "resumenJustificacion": "Explicación breve del criterio usado",
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
