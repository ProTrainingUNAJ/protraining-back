using System.Diagnostics;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
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
    private readonly ICurrentUser? _currentUser;
    private readonly IAiAuditService? _auditService;
    private readonly ILogger<ResilientAiService> _logger;

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        ILogger<ResilientAiService> logger)
        : this(ollamaClient, geminiClient, ruleBasedClient, null, null, null, null, logger)
    {
    }

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        ILogger<ResilientAiService> logger)
        : this(ollamaClient, geminiClient, ruleBasedClient, semanticValidator, null, null, null, logger)
    {
    }

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ILogger<ResilientAiService> logger)
        : this(ollamaClient, geminiClient, ruleBasedClient, semanticValidator, responseCache, null, null, logger)
    {
    }

    public ResilientAiService(
        OllamaClient ollamaClient,
        GeminiClient geminiClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        ILogger<ResilientAiService> logger)
        : this([ollamaClient, geminiClient, ruleBasedClient], semanticValidator, responseCache, currentUser, auditService, logger)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ILogger<ResilientAiService> logger)
        : this(providers, semanticValidator, responseCache, null, null, logger)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        ILogger<ResilientAiService> logger)
    {
        _providers = providers;
        _semanticValidator = semanticValidator;
        _responseCache = responseCache;
        _currentUser = currentUser;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalStopwatch = Stopwatch.StartNew();

        // 1. Verificación de caché en memoria de IA por hash de las instrucciones aislado por PF (KAN-368)
        string? promptHash = null;
        if (_responseCache != null)
        {
            var pfId = _currentUser?.PfId;
            promptHash = _responseCache.ComputePromptHash(request.Goal, request.MuscleGroupEmphasis, pfId);
            if (_responseCache.TryGet(promptHash, out var cachedResponse) && cachedResponse != null)
            {
                totalStopwatch.Stop();
                _logger.LogInformation(
                    "Caché HIT: Respuesta de IA recuperada desde memoria para el objetivo '{Goal}' (Hash: {Hash}, PF: {PfId}) en {ElapsedMs} ms. Evitando llamadas a proveedores externos.",
                    request.Goal,
                    promptHash,
                    pfId?.ToString() ?? "global",
                    totalStopwatch.ElapsedMilliseconds);

                var providerUsedWithCache = cachedResponse.ProviderUsed.EndsWith("(cache)", StringComparison.OrdinalIgnoreCase)
                    ? cachedResponse.ProviderUsed
                    : $"{cachedResponse.ProviderUsed} (cache)";

                // Auditoría de IA para trazabilidad en la tabla de logs
                if (_auditService != null)
                {
                    try
                    {
                        var auditPrompt = $"Goal: {request.Goal} | Muscles: {string.Join(", ", request.MuscleGroupEmphasis ?? [])}";
                        await _auditService.LogAsync(AiLog.Create(
                            provider: providerUsedWithCache,
                            prompt: auditPrompt,
                            latencyMs: totalStopwatch.ElapsedMilliseconds,
                            isValid: true,
                            validationResult: "CacheHit",
                            response: cachedResponse.JustificationSummary,
                            pfId: pfId,
                            athleteId: request.AthleteId), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo registrar la auditoría de IA para el acierto en caché.");
                    }
                }

                return cachedResponse with
                {
                    AthleteId = request.AthleteId,
                    ProviderUsed = providerUsedWithCache
                };
            }
        }

        _logger.LogInformation("Iniciando cadena de proveedores de IA para el atleta '{AthleteId}'.", request.AthleteId);

        for (var i = 0; i < _providers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                if (_semanticValidator != null)
                {
                    var validationResult = await _semanticValidator.ValidateDraftAsync(response, cancellationToken: cancellationToken);
                    if (validationResult.RequiresDeterministicFallback && !isLast)
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
                // Se guardan ÚNICAMENTE respuestas generadas por modelos de IA reales (Ollama o Gemini),
                // excluyendo deliberadamente las reglas determinísticas para no degradar el servicio si la IA se recupera.
                if (_responseCache != null &&
                    !string.IsNullOrEmpty(promptHash) &&
                    provider is not RuleBasedAiClient &&
                    !response.ProviderUsed.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
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
