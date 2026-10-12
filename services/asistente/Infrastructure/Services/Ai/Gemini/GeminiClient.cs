using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Ai.Gemini;

public sealed class GeminiClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiClient> logger) : IAiClient
{
    private readonly GeminiOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string ProviderName => "Gemini";

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("API Key de Gemini no configurada. Omitiendo llamada a Gemini y pasando al respaldo.");
            return null;
        }

        try
        {
            var prompt = BuildAnonymousPrompt(request);
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

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent?key={_options.ApiKey}";
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await httpClient.PostAsync(url, content, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Se agotó la cuota de solicitudes de la capa gratuita de Gemini (HTTP 429). Pasando al siguiente respaldo.");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                logger.LogWarning("Gemini respondió con error {StatusCode}: {ErrorBody}. Activando respaldo.", response.StatusCode, errorBody);
                return null;
            }

            var responseString = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ParseGeminiResponse(request.AthleteId, responseString);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("La llamada a Gemini superó el tiempo límite de {Seconds} segundos. Activando respaldo.", _options.TimeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error al comunicarse con la API de Gemini: {Message}. Activando respaldo.", ex.Message);
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

    private RoutineSuggestionResponseDto? ParseGeminiResponse(Guid athleteId, string jsonResponse)
    {
        using var document = JsonDocument.Parse(jsonResponse);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            logger.LogWarning("La respuesta de Gemini no contuvo candidatos válidos.");
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
            return null;
        }

        var parsed = JsonSerializer.Deserialize<RawRoutineProposal>(text, JsonOptions);
        if (parsed == null || parsed.Exercises == null || parsed.Exercises.Count == 0)
        {
            logger.LogWarning("No se pudo deserializar la rutina generada por Gemini.");
            return null;
        }

        var exercises = parsed.Exercises.Select(e => new SuggestedExerciseDto(
            ExerciseName: e.ExerciseName ?? "Ejercicio general",
            Sets: e.Sets > 0 ? e.Sets : 3,
            Reps: e.Reps > 0 ? e.Reps : 10,
            SuggestedPercentage: e.SuggestedPercentage > 0 ? e.SuggestedPercentage : 65m
        )).ToList();

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
