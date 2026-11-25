using Application.Common.PlanDrafts;
using Application.Common.DayAdjustments;
using Application.Common.Recommendations;
using Application.DTOs.DayAdjustments;
using Application.DTOs.PlanDrafts;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Domain.Exceptions;
using System.Diagnostics;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai;

/// <summary>
/// Orquesta la cadena resiliente de proveedores de IA para el Asistente:
/// 1. Agente de IA con OpenAI (Azure Gateway gpt-4.1-mini)
/// 2. Agente de IA con Google Gemini (respaldo en la nube)
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
    private readonly IExerciseCatalogService? _catalogService;
    private readonly IAiQuotaService? _quotaService;
    private readonly IStructureAthleteClient? _structureAthleteClient;
    private readonly IIdentityHabitsRecommendationsClient? _identityHabitsClient;
    private readonly ILogger<ResilientAiService> _logger;

    public ResilientAiService(
        IAiClient primaryClient,
        IAiClient secondaryClient,
        RuleBasedAiClient ruleBasedClient,
        ILogger<ResilientAiService> logger)
        : this(primaryClient, secondaryClient, ruleBasedClient, null, null, null, null, null, null, logger)
    {
    }

    public ResilientAiService(
        IAiClient primaryClient,
        IAiClient secondaryClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        ILogger<ResilientAiService> logger)
        : this(primaryClient, secondaryClient, ruleBasedClient, semanticValidator, null, null, null, null, null, logger)
    {
    }

    public ResilientAiService(
        IAiClient primaryClient,
        IAiClient secondaryClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ILogger<ResilientAiService> logger)
        : this(primaryClient, secondaryClient, ruleBasedClient, semanticValidator, responseCache, null, null, null, null, logger)
    {
    }

    public ResilientAiService(
        IAiClient primaryClient,
        IAiClient secondaryClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        ILogger<ResilientAiService> logger)
        : this([primaryClient, secondaryClient, ruleBasedClient], semanticValidator, responseCache, currentUser, auditService, null, null, logger)
    {
    }

    public ResilientAiService(
        IAiClient primaryClient,
        IAiClient secondaryClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        IExerciseCatalogService? catalogService,
        ILogger<ResilientAiService> logger)
        : this([primaryClient, secondaryClient, ruleBasedClient], semanticValidator, responseCache, currentUser, auditService, catalogService, null, logger)
    {
    }

    public ResilientAiService(
        IAiClient primaryClient,
        IAiClient secondaryClient,
        RuleBasedAiClient ruleBasedClient,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        IExerciseCatalogService? catalogService,
        IAiQuotaService? quotaService,
        ILogger<ResilientAiService> logger,
        IStructureAthleteClient? structureAthleteClient = null,
        IIdentityHabitsRecommendationsClient? identityHabitsClient = null)
        : this([primaryClient, secondaryClient, ruleBasedClient], semanticValidator, responseCache, currentUser, auditService, catalogService, quotaService, logger, structureAthleteClient, identityHabitsClient)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ILogger<ResilientAiService> logger)
        : this(providers, semanticValidator, responseCache, null, null, null, null, logger)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        ILogger<ResilientAiService> logger)
        : this(providers, semanticValidator, responseCache, currentUser, auditService, null, null, logger)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        IExerciseCatalogService? catalogService,
        ILogger<ResilientAiService> logger,
        IStructureAthleteClient? structureAthleteClient = null)
        : this(providers, semanticValidator, responseCache, currentUser, auditService, catalogService, null, logger, structureAthleteClient)
    {
    }

    public ResilientAiService(
        IReadOnlyList<IAiClient> providers,
        IExerciseSemanticValidator? semanticValidator,
        IAiResponseCache? responseCache,
        ICurrentUser? currentUser,
        IAiAuditService? auditService,
        IExerciseCatalogService? catalogService,
        IAiQuotaService? quotaService,
        ILogger<ResilientAiService> logger,
        IStructureAthleteClient? structureAthleteClient = null,
        IIdentityHabitsRecommendationsClient? identityHabitsClient = null)
    {
        _providers = providers;
        _semanticValidator = semanticValidator;
        _responseCache = responseCache;
        _currentUser = currentUser;
        _auditService = auditService;
        _catalogService = catalogService;
        _quotaService = quotaService;
        _structureAthleteClient = structureAthleteClient;
        _identityHabitsClient = identityHabitsClient;
        _logger = logger;
    }

    public async Task<RoutineSuggestionResponseDto> SuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalStopwatch = Stopwatch.StartNew();
        var pfId = _currentUser?.PfId;

        // 1. Verificación de caché en memoria de IA por hash de las instrucciones aislado por PF (KAN-368)
        string? promptHash = null;
        if (_responseCache != null &&
            request.AdaptationContext is null &&
            request.PreviousPfDecisions is not { Count: > 0 })
        {
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

        // 2. Enriquecer la solicitud con los ejercicios disponibles del catálogo oficial (#087 · KAN-382)
        if (request.AvailableExercises == null && _catalogService != null)
        {
            try
            {
                var allExercises = await _catalogService.GetAllAsync(cancellationToken);
                if (allExercises is { Count: > 0 })
                {
                    var filtered = RoutinePromptBuilder.FilterRelevantExercises(allExercises, request.MuscleGroupEmphasis);
                    request = request with { AvailableExercises = filtered };
                    _logger.LogInformation(
                        "Se enriqueció la solicitud con {Count} ejercicios del catálogo oficial para armar el prompt de IA.",
                        filtered.Count);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudieron obtener ejercicios del catálogo oficial desde el microservicio de Entrenamiento. Continuando con prompt general.");
            }
        }

        // 3. Verificación de cuota diaria en memoria por PF (KAN-510)
        var hasQuota = _quotaService?.HasQuota(pfId) ?? true;
        if (!hasQuota)
        {
            _logger.LogWarning(
                "El preparador físico {PfId} ha alcanzado el límite diario de pedidos a la IA. Derivando a reglas determinísticas.",
                pfId);

            var ruleClient = _providers.OfType<RuleBasedAiClient>().FirstOrDefault() ?? _providers.Last();
            var fallbackTierStopwatch = Stopwatch.StartNew();
            var fallbackResponse = await ruleClient.TrySuggestRoutineAsync(request, cancellationToken);
            fallbackTierStopwatch.Stop();
            totalStopwatch.Stop();

            if (fallbackResponse != null)
            {
                const string quotaNotice = "Llegaste al límite de pedidos a la IA por hoy. Se generó una propuesta basada en reglas determinísticas.";
                var warnings = fallbackResponse.AdaptationWarnings?.ToList() ?? new List<string>();
                if (!warnings.Contains(quotaNotice))
                {
                    warnings.Insert(0, quotaNotice);
                }

                if (_auditService != null)
                {
                    try
                    {
                        await _auditService.LogAsync(AiLog.Create(
                            provider: "Reglas",
                            prompt: $"Goal: {request.Goal} (Límite de IA alcanzado)",
                            latencyMs: fallbackTierStopwatch.ElapsedMilliseconds,
                            isValid: true,
                            validationResult: "QuotaExceededFallback",
                            response: fallbackResponse.JustificationSummary,
                            pfId: pfId,
                            athleteId: request.AthleteId), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error al auditar fallback por cuota de IA.");
                    }
                }

                return fallbackResponse with
                {
                    ProviderUsed = "Reglas (Límite diario alcanzado)",
                    AdaptationWarnings = warnings
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
                    var validationResult = await _semanticValidator.ValidateDraftAsync(
                        response,
                        adaptationContext: request.AdaptationContext,
                        cancellationToken: cancellationToken);
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
                // Se guardan ÚNICAMENTE respuestas generadas por modelos de IA reales (OpenAI o Gemini),
                // excluyendo deliberadamente las reglas determinísticas para no degradar el servicio si la IA se recupera.
                if (_responseCache != null &&
                    request.AdaptationContext is null &&
                    !string.IsNullOrEmpty(promptHash) &&
                    provider is not RuleBasedAiClient &&
                    !response.ProviderUsed.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
                {
                    _responseCache.Set(promptHash, response);
                }

                // 3. Registrar consumo de cuota diaria de IA si la respuesta provino de un proveedor de IA real (KAN-510)
                if (provider is not RuleBasedAiClient &&
                    !response.ProviderUsed.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
                {
                    _quotaService?.RecordUsage(pfId);
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

    public async Task<(PlanDraftContentDto Content, string Provider)> GeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Iniciando generación de borrador de planificación multisesión para objetivo '{Goal}' ({Weeks} semanas, {Days} días/semana).",
            request.Goal, request.Weeks, request.DaysPerWeek);

        var totalStopwatch = Stopwatch.StartNew();

        // Verificación de cuota diaria en memoria por PF (KAN-510)
        var pfId = _currentUser?.PfId;
        var hasQuota = _quotaService?.HasQuota(pfId) ?? true;
        if (!hasQuota)
        {
            _logger.LogWarning(
                "El preparador físico {PfId} ha alcanzado el límite diario de IA para generación de planes. Derivando a reglas determinísticas.",
                pfId);

            var ruleClient = _providers.OfType<RuleBasedAiClient>().FirstOrDefault() ?? _providers.Last();
            var fallbackContent = await ruleClient.TryGeneratePlanDraftAsync(request, matchDates, cancellationToken);
            totalStopwatch.Stop();

            if (fallbackContent != null)
            {
                if (_auditService != null)
                {
                    try
                    {
                        await _auditService.LogAsync(AiLog.Create(
                            provider: "Reglas",
                            prompt: $"PlanDraft: {request.Goal} (Límite de IA alcanzado)",
                            latencyMs: 0,
                            isValid: true,
                            validationResult: "QuotaExceededFallback",
                            athleteId: request.AthleteId,
                            pfId: pfId), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error al auditar fallback de plan por cuota de IA.");
                    }
                }

                return (fallbackContent, "Reglas (Límite diario alcanzado)");
            }
        }

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isLast = i == _providers.Count - 1;
            var tierStopwatch = Stopwatch.StartNew();

            try
            {
                var content = await provider.TryGeneratePlanDraftAsync(request, matchDates, cancellationToken);
                if (content == null)
                {
                    tierStopwatch.Stop();
                    if (!isLast)
                    {
                        _logger.LogWarning(
                            "El proveedor '{Provider}' no pudo generar el borrador tras {ElapsedMs} ms. Pasando al siguiente respaldo.",
                            provider.ProviderName,
                            tierStopwatch.ElapsedMilliseconds);
                    }
                    continue;
                }

                // Registrar consumo de cuota diaria apenas respondió un modelo de IA real (aunque luego falle validación, gastó tokens - KAN-510)
                if (provider is not RuleBasedAiClient &&
                    !provider.ProviderName.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
                {
                    _quotaService?.RecordUsage(pfId);
                }

                var rawRequestedExercises = GoalConstraintExtractor.ExtractRawRequestedExercisesPerSession(request.Goal);
                var requestedMaxExercises = GoalConstraintExtractor.ExtractMaxExercisesPerSession(request.Goal);
                if (requestedMaxExercises is > 0)
                {
                    IReadOnlyList<CatalogExerciseDto> catalogForAdjustment = [];
                    if (_catalogService != null)
                    {
                        try
                        {
                            var goalCatalog = await _catalogService.GetExercisesForGoalAsync(request.Goal, 25, cancellationToken);
                            if (goalCatalog is not { Count: > 0 })
                            {
                                goalCatalog = await _catalogService.GetAllAsync(cancellationToken);
                            }
                            if (goalCatalog is { Count: > 0 })
                            {
                                catalogForAdjustment = goalCatalog;
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "No se pudo consultar el catálogo filtrado para ajustar la cantidad de ejercicios por sesión.");
                        }
                    }

                    content = PlanDraftBlockParser.EnforceExactExercisesPerSession(
                        content,
                        requestedMaxExercises.Value,
                        rawRequestedExercises,
                        catalogForAdjustment,
                        request.Goal,
                        request.StartDate,
                        matchDates,
                        request.AdaptationContext);
                }

                if (_catalogService != null)
                {
                    try
                    {
                        await PlanDraftContentValidator.ValidateAsync(content, _catalogService, cancellationToken);
                    }
                    catch (DomainException ex)
                    {
                        tierStopwatch.Stop();
                        _logger.LogWarning(
                            "El borrador generado por '{Provider}' fue descartado por fallar validación de catálogo o esquema: {Message}. Pasando al siguiente proveedor.",
                            provider.ProviderName,
                            ex.Message);

                        if (_auditService != null)
                        {
                            await _auditService.LogAsync(AiLog.Create(
                                provider.ProviderName,
                                request.Goal,
                                tierStopwatch.ElapsedMilliseconds,
                                isValid: false,
                                validationResult: "DescartadoPorEsquemaOCatalogo",
                                athleteId: request.AthleteId,
                                errorMessage: ex.Message));
                        }
                        continue;
                    }
                }

                tierStopwatch.Stop();
                totalStopwatch.Stop();
                _logger.LogInformation(
                    "Borrador de planificación generado exitosamente por proveedor '{Provider}' en {ElapsedMs} ms (tiempo total: {TotalMs} ms).",
                    provider.ProviderName,
                    tierStopwatch.ElapsedMilliseconds,
                    totalStopwatch.ElapsedMilliseconds);

                return (content, provider.ProviderName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                tierStopwatch.Stop();
                _logger.LogWarning(ex, "Excepción en proveedor '{Provider}' al generar borrador de planificación. Pasando al siguiente respaldo.", provider.ProviderName);
            }
        }

        totalStopwatch.Stop();
        _logger.LogError("Los tres proveedores de IA fallaron al generar el borrador de planificación tras {ElapsedMs} ms.", totalStopwatch.ElapsedMilliseconds);

        throw new AiServiceUnavailableException("El servicio de generación de planificaciones con inteligencia artificial no se encuentra disponible. Por favor intente más tarde.");
    }

    public async Task<(string Justification, string Provider)> GenerateDayAdjustmentJustificationAsync(
        IReadOnlyList<DayAdjustmentItemDto> items,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Verificación de cuota diaria en memoria por PF (KAN-510)
        var pfId = _currentUser?.PfId;
        var hasQuota = _quotaService?.HasQuota(pfId) ?? true;
        if (!hasQuota)
        {
            _logger.LogWarning(
                "El preparador físico {PfId} ha alcanzado el límite diario de IA para redacción de justificaciones. Derivando a reglas determinísticas.",
                pfId);
            return (DayAdjustmentMapper.BuildDeterministicJustification(items), "Reglas (Límite diario alcanzado)");
        }

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isLast = i == _providers.Count - 1;
            var tierStopwatch = Stopwatch.StartNew();

            try
            {
                var justification = await provider.TryGenerateDayAdjustmentJustificationAsync(items, cancellationToken);
                tierStopwatch.Stop();

                if (!string.IsNullOrWhiteSpace(justification))
                {
                    if (provider is not RuleBasedAiClient &&
                        !provider.ProviderName.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
                    {
                        _quotaService?.RecordUsage(pfId);
                    }

                    return (justification.Trim(), provider.ProviderName);
                }

                if (!isLast)
                {
                    _logger.LogWarning(
                        "El proveedor '{Provider}' no pudo redactar la justificación del ajuste del día tras {ElapsedMs} ms. Pasando al siguiente respaldo.",
                        provider.ProviderName,
                        tierStopwatch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                tierStopwatch.Stop();
                _logger.LogWarning(ex, "Excepción en proveedor '{Provider}' al redactar justificación de ajuste del día. Pasando al siguiente respaldo.", provider.ProviderName);
            }
        }

        return (DayAdjustmentMapper.BuildDeterministicJustification(items), "Reglas");
    }

    public async Task<TrainingRecommendationResponseDto> GenerateTrainingRecommendationAsync(
        TrainingRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Verificación de cuota diaria en memoria por PF (KAN-510)
        var pfId = _currentUser?.PfId;
        var hasQuota = _quotaService?.HasQuota(pfId) ?? true;
        if (!hasQuota)
        {
            _logger.LogWarning(
                "El preparador físico {PfId} ha alcanzado el límite diario de IA para recomendaciones de entrenamiento. Derivando a reglas determinísticas.",
                pfId);
            var quotaFallback = RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(request, "Reglas (Límite diario alcanzado)");
            return quotaFallback;
        }

        var prompt = RecommendationPromptBuilder.BuildTrainingRecommendationPrompt(request);

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isLast = i == _providers.Count - 1;
            var tierStopwatch = Stopwatch.StartNew();

            try
            {
                var candidate = await provider.TryGenerateTrainingRecommendationAsync(request, cancellationToken);
                tierStopwatch.Stop();

                if (candidate != null)
                {
                    var validation = RecommendationOutputValidator.ValidateTrainingDto(candidate, request);
                    if (validation.IsValid && validation.Value != null)
                    {
                        if (provider is not RuleBasedAiClient &&
                            !provider.ProviderName.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
                        {
                            _quotaService?.RecordUsage(pfId);
                        }

                        return validation.Value with { ProviderUsed = provider.ProviderName };
                    }

                    _logger.LogWarning(
                        "La recomendación de entrenamiento de '{Provider}' fue descartada por validación ({ValidationCode}): {Error}. Pasando al siguiente respaldo.",
                        provider.ProviderName,
                        validation.ValidationCode,
                        validation.ErrorMessage);

                    if (_auditService != null)
                    {
                        await _auditService.LogAsync(AiLog.Create(
                            provider.ProviderName,
                            prompt,
                            tierStopwatch.ElapsedMilliseconds,
                            isValid: false,
                            validationResult: validation.ValidationCode,
                            response: candidate.RecommendationText,
                            pfId: _currentUser?.PfId,
                            athleteId: request.AthleteId,
                            errorMessage: validation.ErrorMessage), cancellationToken);
                    }

                    continue;
                }

                if (!isLast)
                {
                    _logger.LogWarning(
                        "El proveedor '{Provider}' no pudo redactar la recomendación de entrenamiento tras {ElapsedMs} ms. Pasando al siguiente respaldo.",
                        provider.ProviderName,
                        tierStopwatch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                tierStopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "Excepción en proveedor '{Provider}' al redactar recomendación de entrenamiento. Pasando al siguiente respaldo.",
                    provider.ProviderName);
            }
        }

        var fallback = RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(request, "Reglas");
        var fallbackValidation = RecommendationOutputValidator.ValidateTrainingDto(fallback, request);
        if (!fallbackValidation.IsValid || fallbackValidation.Value is null)
        {
            throw new DomainException(fallbackValidation.ErrorMessage ?? "No se pudo validar la recomendación de entrenamiento.");
        }

        return fallbackValidation.Value;
    }

    public async Task<NutritionRecommendationResponseDto> GenerateNutritionRecommendationAsync(
        NutritionRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Objetivo orientado a bajar de peso => no se bloquea la generación; se debe producir
        // una recomendación segura sin cantidades ni estrategias para perder peso y con derivación.
        // La seguridad se refuerza en prompt, generación determinística y validación.
        if (RecommendationOutputValidator.TryFindForbiddenWeightLoss(request.AthleticGoal, out _))
        {
            request = request with
            {
                IncludeQuantities = false,
                MinProteinGrams = null,
                MaxProteinGrams = null,
                DailyProteinRequirementGrams = null,
                RecommendationMode = HabitsRecommendationRules.ModeReferral,
                RequiresProfessionalReferral = true,
                ReferralReasons = (request.ReferralReasons ?? [])
                    .Concat(["Objetivo orientado a bajar de peso: no se brindan cantidades ni estrategias para perder peso; se sugiere derivación profesional."])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        if (request.ApprovedFoods is { Count: 0 })
        {
            throw new DomainException("La lista de alimentos aprobados debe contener al menos un alimento.");
        }

        // Requiere atleta identificado para toda recomendación de alimentación (#201 · KAN-496):
        // sin atleta no se puede verificar el permiso del tutor, así que no se genera nada (fail-closed).
        if (!request.AthleteId.HasValue)
        {
            _logger.LogWarning(
                "No se indicó AthleteId para la recomendación de alimentación. No se generará recomendación (fail-closed, KAN-496).");
            throw new DomainException(
                "Se requiere el identificador del atleta para generar una recomendación de alimentación.");
        }

        // KAN-493: la preferencia del PF se verifica antes de consultar a cualquier proveedor de IA.
        // Si Identidad no responde o devuelve false, no se genera nada (fail-closed).
        var pfEnabled = _identityHabitsClient is not null
            ? await _identityHabitsClient.GetMyHabitsRecommendationsEnabledAsync(cancellationToken)
            : null;

        if (pfEnabled is not true)
        {
            _logger.LogWarning(
                "La preferencia de recomendaciones del PF está desactivada o no se pudo consultar. No se generará recomendación de alimentación (fail-closed).");
            throw new DomainException("Las recomendaciones de hábitos y alimentación están deshabilitadas para este preparador físico.");
        }

        // Verificación del permiso del tutor en atletas menores (#201 · KAN-496):
        // si no se puede verificar o el tutor no autorizó, no se genera ninguna recomendación (fail-closed).
        var permission = _structureAthleteClient is not null
            ? await _structureAthleteClient.GetHabitsPermissionAsync(request.AthleteId.Value, cancellationToken)
            : null;

        if (permission is null)
        {
            _logger.LogWarning(
                "No se pudo verificar el permiso del tutor del atleta {AthleteId}. No se generará recomendación de alimentación (fail-closed).",
                request.AthleteId.Value);
            throw new DomainException(
                $"No se pudo verificar el permiso del tutor del atleta {request.AthleteId.Value}; no se generará recomendación de alimentación.");
        }

        if (!HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission))
        {
            _logger.LogWarning(
                "El atleta {AthleteId} no tiene habilitadas las recomendaciones (permiso del tutor no verificado o no autorizado). No se generará recomendación de alimentación (KAN-493 / KAN-496).",
                request.AthleteId.Value);
            throw new DomainException("El atleta no tiene autorizadas las recomendaciones de hábitos y alimentación.");
        }

        // Menores de edad: aunque el tutor autorice, nunca se incluyen cantidades (KAN-378 / KAN-496).
        // Se fuerza el modo cualitativo antes de armar el prompt, validar y generar el respaldo por reglas.
        if (permission.IsMinor)
        {
            request = request with { IncludeQuantities = false };
        }

        // Verificación de cuota diaria en memoria por PF (KAN-510)
        var pfId = _currentUser?.PfId;
        var hasQuota = _quotaService?.HasQuota(pfId) ?? true;
        if (!hasQuota)
        {
            _logger.LogWarning(
                "El preparador físico {PfId} ha alcanzado el límite diario de IA para recomendaciones de nutrición. Derivando a reglas determinísticas.",
                pfId);
            var quotaFallback = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request, "Reglas (Límite diario alcanzado)");
            return quotaFallback;
        }

        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);

        for (var i = 0; i < _providers.Count; i++)
        {
            var provider = _providers[i];
            var isLast = i == _providers.Count - 1;
            var tierStopwatch = Stopwatch.StartNew();

            try
            {
                var candidate = await provider.TryGenerateNutritionRecommendationAsync(request, cancellationToken);
                tierStopwatch.Stop();

                if (candidate != null)
                {
                    var validation = RecommendationOutputValidator.ValidateNutritionDto(candidate, request);
                    if (validation.IsValid && validation.Value != null)
                    {
                        if (provider is not RuleBasedAiClient &&
                            !provider.ProviderName.StartsWith("Reglas", StringComparison.OrdinalIgnoreCase))
                        {
                            _quotaService?.RecordUsage(pfId);
                        }

                        return validation.Value with { ProviderUsed = provider.ProviderName };
                    }

                    _logger.LogWarning(
                        "La recomendación de alimentación de '{Provider}' fue descartada por validación ({ValidationCode}): {Error}. Pasando al siguiente respaldo.",
                        provider.ProviderName,
                        validation.ValidationCode,
                        validation.ErrorMessage);

                    if (_auditService != null)
                    {
                        await _auditService.LogAsync(AiLog.Create(
                            provider.ProviderName,
                            prompt,
                            tierStopwatch.ElapsedMilliseconds,
                            isValid: false,
                            validationResult: validation.ValidationCode,
                            response: candidate.SuggestedPlateComposition,
                            pfId: _currentUser?.PfId,
                            athleteId: request.AthleteId,
                            errorMessage: validation.ErrorMessage), cancellationToken);
                    }

                    continue;
                }

                if (!isLast)
                {
                    _logger.LogWarning(
                        "El proveedor '{Provider}' no pudo redactar la recomendación de alimentación tras {ElapsedMs} ms. Pasando al siguiente respaldo.",
                        provider.ProviderName,
                        tierStopwatch.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                tierStopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "Excepción en proveedor '{Provider}' al redactar recomendación de alimentación. Pasando al siguiente respaldo.",
                    provider.ProviderName);
            }
        }

        var fallback = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request, "Reglas");
        var fallbackValidation = RecommendationOutputValidator.ValidateNutritionDto(fallback, request);
        if (!fallbackValidation.IsValid || fallbackValidation.Value is null)
        {
            throw new DomainException(fallbackValidation.ErrorMessage ?? "No se pudo validar la recomendación de alimentación.");
        }

        return fallbackValidation.Value;
    }
}
