using System.Diagnostics;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai;

/// <summary>
/// Orquesta la cadena resiliente de proveedores de IA para el Asistente:
/// 1. Ollama (modelo local en Docker)
/// 2. Google Gemini (respaldo en la nube)
/// 3. Motor de reglas determinísticas locales (respaldo final garantizado)
/// Asegura alta disponibilidad, tiempos límites independientes por proveedor, validación semántica de ejercicios y registro del origen.
/// </summary>
public sealed class ResilientAiService : IAiService
{
    private readonly IReadOnlyList<IAiClient> _providers;
    private readonly IExerciseSemanticValidator? _semanticValidator;
    private readonly IAiResponseCache? _responseCache;
    private readonly ILogger<ResilientAiService> _logger;

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        ILogger<ResilientAiService> logger)
        : this(ollamaClient, geminiClient, ruleBasedClient, null, null, logger)
    {
    }

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        ILogger<ResilientAiService> logger)
        : this(ollamaClient, geminiClient, ruleBasedClient, semanticValidator, null, logger)
    {
    }

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ILogger<ResilientAiService> logger)
        : this([ollamaClient, geminiClient, ruleBasedClient], semanticValidator, responseCache, logger)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ILogger<ResilientAiService> logger)
    {
        _providers = providers;
        _semanticValidator = semanticValidator;
        _responseCache = responseCache;
        _logger = logger;
    }

    public async Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();

        // 1. Verificación de caché en memoria de IA por hash de las instrucciones (KAN-368)
        string? promptHash = null;
        if (_responseCache != null)
        {
            promptHash = _responseCache.ComputePromptHash(request.Goal, request.MuscleGroupEmphasis);
            if (_responseCache.TryGet(promptHash, out var cachedResponse) && cachedResponse != null)
            {
                totalStopwatch.Stop();
                _logger.LogInformation(
                    "Caché HIT: Respuesta de IA recuperada desde memoria para el objetivo '{Goal}' (Hash: {Hash}) en {ElapsedMs} ms. Evitando llamadas a proveedores externos.",
                    request.Goal,
                    promptHash,
                    totalStopwatch.ElapsedMilliseconds);

                return cachedResponse with
                {
                    AthleteId = request.AthleteId
                };
            }
        }

        _logger.LogInformation("Iniciando cadena de proveedores de IA para el atleta '{AthleteId}'.", request.AthleteId);

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isLast = i == _providers.Count - 1;
            var tierNumber = i + 1;

            _logger.LogInformation(
                "Paso {Step} de {Total}: Intentando con proveedor '{Provider}'.",
                tierNumber,
                _providers.Count,
                provider.ProviderName);

            var tierStopwatch = Stopwatch.StartNew();
            RoutineSuggestionResponseDto? response = null;

            try
            {
                response = await provider.TrySuggestRoutineAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Excepción no controlada durante la consulta a {Provider} tras {ElapsedMs} ms.",
                    provider.ProviderName,
                    tierStopwatch.ElapsedMilliseconds);
            }
            finally
            {
                tierStopwatch.Stop();
            }

            if (response != null)
            {
                // Validación semántica de ejercicios en caso de contar con el validador
                if (_semanticValidator != null && !isLast)
                {
                    var validationResult = await _semanticValidator.ValidateDraftAsync(response, cancellationToken: cancellationToken);
                    if (validationResult.RequiresDeterministicFallback)
                    {
                        _logger.LogWarning(
                            "La propuesta del proveedor '{Provider}' contiene ejercicios inválidos que no pudieron corregirse ({Messages}). Descartando propuesta y activando siguiente respaldo.",
                            provider.ProviderName,
                            string.Join("; ", validationResult.ValidationMessages));
                        continue;
                    }

                    response = validationResult.SanitizedRoutine;
                }

                // 2. Almacenar la propuesta validada en caché en memoria (KAN-368)
                if (_responseCache != null && !string.IsNullOrEmpty(promptHash))
                {
                    _responseCache.Set(promptHash, response);
                }

                totalStopwatch.Stop();
                _logger.LogInformation(
                    "Propuesta de rutina resuelta exitosamente por proveedor '{Provider}' en {ElapsedMs} ms (tiempo total: {TotalMs} ms).",
                    response.ProviderUsed,
                    tierStopwatch.ElapsedMilliseconds,
                    totalStopwatch.ElapsedMilliseconds);
                return response;
            }

            if (!isLast)
            {
                _logger.LogWarning(
                    "El proveedor '{Provider}' no estuvo disponible, agotó cuota o superó su tiempo límite tras {ElapsedMs} ms. Activando siguiente respaldo.",
                    provider.ProviderName,
                    tierStopwatch.ElapsedMilliseconds);
            }
        }

        totalStopwatch.Stop();
        throw new InvalidOperationException("Ningún proveedor de la cadena resiliente pudo generar la rutina.");
    }
}
