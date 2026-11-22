using System.Diagnostics;
using System.Globalization;
using Application.Common.Ai;
using Application.Common.DayAdjustments;
using Application.Common.Helpers;
using Application.Common.Recommendations;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.DTOs.DayAdjustments;
using Application.DTOs.PlanDrafts;
using Application.DTOs.Recommendations;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Ai.Rules;

public sealed class RuleBasedAiClient(
    IAiAuditService aiAuditService,
    ILogger<RuleBasedAiClient> logger,
    IExerciseCatalogService? catalogService = null,
    ITrainingPlanServiceClient? trainingPlanServiceClient = null) : IAiClient
{
    public string ProviderName => "Reglas";

    private const int ExercisesPerSession = 5;

    public async Task<RoutineSuggestionResponseDto?> TrySuggestRoutineAsync(
        RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Generando propuesta de entrenamiento mediante motor de reglas determinísticas para el objetivo: '{Goal}'.", request.Goal);

        var (title, justification, exercises) = DeterministicRuleEngine.GenerateProposal(
            request.Goal,
            request.MuscleGroupEmphasis);

        stopwatch.Stop();

        var prompt = $"Objetivo: {request.Goal}. Énfasis muscular: {(request.MuscleGroupEmphasis is { Count: > 0 } ? string.Join(", ", request.MuscleGroupEmphasis) : "General")}";

        var response = new RoutineSuggestionResponseDto(
            AthleteId: request.AthleteId,
            ProposedTitle: title,
            JustificationSummary: justification,
            Exercises: exercises,
            ProviderUsed: ProviderName
        );

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: title,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0));

        return response;
    }

    public async Task<PlanDraftContentDto?> TryGeneratePlanDraftAsync(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        logger?.LogInformation("Generando borrador de planificación mediante motor de reglas determinísticas para '{Goal}' ({Weeks} semanas, {Days} días/sem).",
            request.Goal, request.Weeks, request.DaysPerWeek);

        IReadOnlyList<CatalogExerciseDto> catalog = [];
        if (catalogService != null)
        {
            var allExercises = await catalogService.GetAllAsync(cancellationToken);
            if (allExercises is { Count: > 0 })
            {
                catalog = allExercises;
            }
            else
            {
                var goalExercises = await catalogService.GetExercisesForGoalAsync(request.Goal, 25, cancellationToken);
                if (goalExercises is { Count: > 0 })
                {
                    catalog = goalExercises;
                }
            }
        }

        // KAN-563: Si el catálogo está vacío, no inventar IDs de respaldo y retornar null
        if (catalog == null || catalog.Count == 0)
        {
            logger?.LogWarning("Catálogo de ejercicios vacío. No se puede generar borrador con reglas sin ejercicios reales.");
            return null;
        }

        // KAN-605: Detectar objetivo según tabla deportiva y armar reparto equilibrado o foco pedido por el PF
        var profile = RuleBasedGoalProfiles.Detect(request.Goal);
        var selectedDays = ResolveTrainingDays(request.TrainingDays, request.DaysPerWeek);
        var startDate = request.StartDate ?? ArgentinaTime.Today;
        var maxExercisesPerSession = GoalConstraintExtractor.ExtractMaxExercisesPerSession(request.Goal) ?? ExercisesPerSession;

        var splitPlan = RuleBasedSplitPlanner.BuildSplit(
            catalog,
            request.Goal,
            selectedDays.Count,
            maxExercisesPerSession);

        // KAN-605: Si el borrador es para un atleta con molestias activas, aplicar restricciones y sustitutos
        var adaptedSessions = splitPlan.Sessions;
        var totalSubstitutions = 0;
        string? restrictionsWarning = null;
        if (request.AthleteId.HasValue && request.AthleteId.Value != Guid.Empty)
        {
            (adaptedSessions, totalSubstitutions, restrictionsWarning) = await ApplyAthleteRestrictionsAsync(
                splitPlan.Sessions,
                catalog,
                request.AdaptationContext,
                cancellationToken);
        }

        var weeks = RuleBasedPlanProgression.GenerateWeeks(
            adaptedSessions,
            selectedDays,
            profile,
            request.Weeks,
            startDate,
            matchDates);

        stopwatch.Stop();

        var matchCount = CountRelevantMatches(matchDates, startDate, request.Weeks);
        var justification = BuildJustificationSummary(
            profile,
            splitPlan.SplitDescription,
            request.Weeks,
            matchCount,
            totalSubstitutions,
            restrictionsWarning);

        var content = new PlanDraftContentDto(
            Name: $"Planificación {request.Goal}",
            Objective: request.Goal,
            JustificationSummary: justification,
            Weeks: weeks
        );

        var prompt = $"Objetivo: {request.Goal}. Semanas: {request.Weeks}. Días: {request.DaysPerWeek}. Partidos: {matchCount}";
        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: content.Name,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0));

        return content;
    }

    private async Task<(IReadOnlyList<RuleSessionBlueprint> Sessions, int SubstitutionCount, string? RestrictionsWarning)> ApplyAthleteRestrictionsAsync(
        IReadOnlyList<RuleSessionBlueprint> sessions,
        IReadOnlyList<CatalogExerciseDto> catalog,
        AthleteAdaptationContextDto? adaptationContext,
        CancellationToken cancellationToken)
    {
        if (adaptationContext is null ||
            (adaptationContext.ActiveInjuries.Count == 0 && adaptationContext.Restrictions.Count == 0))
        {
            return (sessions, 0, null);
        }

        var restrictionsByExerciseId = new Dictionary<Guid, ExerciseRestrictionContextDto>();
        foreach (var restriction in adaptationContext.Restrictions)
        {
            if (restriction.ExerciseId != Guid.Empty && !restrictionsByExerciseId.ContainsKey(restriction.ExerciseId))
            {
                restrictionsByExerciseId[restriction.ExerciseId] = restriction;
            }
        }

        var activeInjuryZones = adaptationContext.ActiveInjuries
            .Select(i => i.Zone?.Trim())
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var primaryInjuryZone = activeInjuryZones.FirstOrDefault();
        var trainingRestrictionsUnavailable = false;

        // Si hay lesiones activas y el cliente de Entrenamiento está disponible, consultar restricciones
        // para los ejercicios elegidos (que podrían no tener 1RM cargado en AdaptationContext)
        if (trainingPlanServiceClient is not null && adaptationContext.ActiveInjuries.Count > 0)
        {
            var painZones = string.Join(
                ",",
                activeInjuryZones.Select(z => $"{z.ToLowerInvariant()}:3"));

            var candidateIds = sessions
                .SelectMany(s => s.Exercises.Concat(s.FallbackPool))
                .Select(e => e.Id)
                .Where(id => id != Guid.Empty && !restrictionsByExerciseId.ContainsKey(id))
                .Distinct()
                .ToList();

            if (!string.IsNullOrWhiteSpace(painZones) && candidateIds.Count > 0)
            {
                try
                {
                    var checkedRestrictions = await trainingPlanServiceClient.CheckExerciseRestrictionsAsync(
                        painZones,
                        candidateIds,
                        cancellationToken);

                    foreach (var checkedItem in checkedRestrictions.Where(r => r.Restricted && r.ExerciseId != Guid.Empty))
                    {
                        if (restrictionsByExerciseId.ContainsKey(checkedItem.ExerciseId))
                            continue;

                        var substitutes = (checkedItem.Substitutes ?? [])
                            .Where(s => s.ExerciseId != Guid.Empty)
                            .Select(s => new AuthorizedSubstituteDto(s.ExerciseId, s.Name))
                            .ToList();

                        var displayZone = !string.IsNullOrWhiteSpace(checkedItem.Zone)
                            ? checkedItem.Zone.Trim()
                            : primaryInjuryZone;

                        restrictionsByExerciseId[checkedItem.ExerciseId] = new ExerciseRestrictionContextDto(
                            checkedItem.ExerciseId,
                            displayZone,
                            checkedItem.Reason,
                            substitutes);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    trainingRestrictionsUnavailable = true;
                    logger?.LogWarning(
                        ex,
                        "No se pudieron consultar restricciones adicionales en Entrenamiento para el atleta {AthleteId}. Se aplicarán las restricciones ya disponibles.",
                        adaptationContext.AthleteId);
                }
            }
        }

        string? unavailableWarning = null;
        if (trainingRestrictionsUnavailable)
        {
            var zonesText = activeInjuryZones.Count > 0
                ? string.Join(", ", activeInjuryZones)
                : "zona afectada";
            unavailableWarning =
                $"Aviso: no se pudo consultar el servicio de Entrenamiento para verificar todas las restricciones por molestia de {zonesText}; revisar los ejercicios y reemplazos antes de aprobar.";
        }

        if (restrictionsByExerciseId.Count == 0)
        {
            if (unavailableWarning is null)
                return (sessions, 0, null);

            var flaggedSessions = sessions
                .Select(s => s with
                {
                    SubstitutionNotes = [.. s.SubstitutionNotes, unavailableWarning]
                })
                .ToList();
            return (flaggedSessions, 0, unavailableWarning);
        }

        var catalogById = catalog
            .Where(e => e.Id != Guid.Empty)
            .GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var adaptedSessions = new List<RuleSessionBlueprint>(sessions.Count);
        var totalSubstitutions = 0;

        foreach (var session in sessions)
        {
            var adaptedExercises = new List<CatalogExerciseDto>(session.Exercises.Count);
            var usedIds = new HashSet<Guid>();
            var notes = new List<string>(session.SubstitutionNotes);

            if (unavailableWarning is not null)
            {
                notes.Add(unavailableWarning);
            }

            foreach (var exercise in session.Exercises)
            {
                if (!restrictionsByExerciseId.TryGetValue(exercise.Id, out var restriction))
                {
                    if (usedIds.Add(exercise.Id))
                    {
                        adaptedExercises.Add(exercise);
                    }
                    continue;
                }

                var zoneLabel = !string.IsNullOrWhiteSpace(restriction.Zone)
                    ? restriction.Zone.Trim()
                    : (primaryInjuryZone ?? "zona afectada");

                // Regla determinística (KAN-452): tomar el primer sustituto autorizado disponible en el catálogo del PF
                CatalogExerciseDto? replacement = null;
                var usedAuthorizedSubstitute = false;
                foreach (var substitute in restriction.Substitutes)
                {
                    if (substitute.ExerciseId == Guid.Empty ||
                        restrictionsByExerciseId.ContainsKey(substitute.ExerciseId) ||
                        usedIds.Contains(substitute.ExerciseId))
                    {
                        continue;
                    }

                    if (catalogById.TryGetValue(substitute.ExerciseId, out var catalogSubstitute))
                    {
                        replacement = catalogSubstitute;
                        usedAuthorizedSubstitute = true;
                        break;
                    }
                }

                // Si ningún sustituto autorizado está libre en el catálogo, buscar alternativa del mismo grupo
                replacement ??= session.FallbackPool.FirstOrDefault(candidate =>
                    !restrictionsByExerciseId.ContainsKey(candidate.Id) &&
                    !usedIds.Contains(candidate.Id));

                if (replacement is not null && usedIds.Add(replacement.Id))
                {
                    adaptedExercises.Add(replacement);
                    if (usedAuthorizedSubstitute)
                    {
                        notes.Add($"Se reemplazó {exercise.Name} por {replacement.Name} por la molestia de {zoneLabel}.");
                    }
                    else
                    {
                        var fallbackContext = trainingRestrictionsUnavailable
                            ? "alternativa del mismo grupo sin validación de Entrenamiento; revisar antes de aprobar"
                            : "alternativa del mismo grupo sin sustituto autorizado disponible; revisar antes de aprobar";
                        notes.Add($"Se reemplazó {exercise.Name} por {replacement.Name} por la molestia de {zoneLabel} ({fallbackContext}).");
                    }
                    totalSubstitutions++;
                }
                else
                {
                    notes.Add($"Se quitó {exercise.Name} por la molestia de {zoneLabel}.");
                    totalSubstitutions++;
                }
            }

            // Completar hasta el tamaño original de la sesión si un sustituto promovido dejó cupo libre
            if (adaptedExercises.Count < session.Exercises.Count)
            {
                foreach (var candidate in session.FallbackPool)
                {
                    if (adaptedExercises.Count >= session.Exercises.Count)
                        break;

                    if (!restrictionsByExerciseId.ContainsKey(candidate.Id) && usedIds.Add(candidate.Id))
                    {
                        adaptedExercises.Add(candidate);
                    }
                }
            }

            // Resguardo ante catálogos muy chicos: si todo el grupo estaba restringido, usar un ejercicio seguro del catálogo del PF
            if (adaptedExercises.Count == 0)
            {
                var safeFallback = catalog.FirstOrDefault(candidate =>
                    candidate.Id != Guid.Empty &&
                    !restrictionsByExerciseId.ContainsKey(candidate.Id) &&
                    usedIds.Add(candidate.Id));

                if (safeFallback is not null)
                {
                    adaptedExercises.Add(safeFallback);
                }
            }

            adaptedSessions.Add(session with
            {
                Exercises = adaptedExercises,
                SubstitutionNotes = notes
            });
        }

        return (adaptedSessions, totalSubstitutions, unavailableWarning);
    }

    private static int CountRelevantMatches(
        IReadOnlyList<DateOnly>? matchDates,
        DateOnly startDate,
        int totalWeeks)
    {
        if (matchDates is not { Count: > 0 })
            return 0;

        var endDate = startDate.AddDays((Math.Clamp(totalWeeks, 1, 8) * 7) - 1);
        return matchDates
            .Distinct()
            .Count(m => m >= startDate && m <= endDate);
    }

    private static string BuildJustificationSummary(
        RuleGoalProfile profile,
        string splitDescription,
        int totalWeeks,
        int matchCount,
        int totalSubstitutions,
        string? restrictionsWarning)
    {
        var culture = CultureInfo.GetCultureInfo("es-AR");
        var basePct = profile.MainBasePercentage.ToString("0.#", culture);
        var maxPct = profile.MainMaxPercentage.ToString("0.#", culture);

        var line1 =
            $"Borrador de {profile.DisplayName} armado por reglas ({splitDescription}), " +
            $"con {profile.MainSets} × {profile.MainReps} al {basePct} % en el principal y accesorios {profile.AccessorySets} × {profile.AccessoryReps}.";

        var progressionPart = totalWeeks <= 1
            ? "Plan de 1 semana con carga base."
            : totalWeeks >= 4
                ? $"Progresión de +2,5 %RM por semana (tope {maxPct} %) y semana {totalWeeks} de descarga (-1 serie, -10 %RM)."
                : $"Progresión de +2,5 %RM por semana en el principal (tope {maxPct} %).";

        var extras = new List<string> { progressionPart };
        if (matchCount > 0)
            extras.Add($"Carga reducida en semanas con partido ({matchCount} partido(s) en el ciclo).");
        if (totalSubstitutions > 0)
            extras.Add($"Incluye {totalSubstitutions} sustitución(es) por molestias activas del atleta.");
        if (!string.IsNullOrWhiteSpace(restrictionsWarning))
            extras.Add(restrictionsWarning);

        return $"{line1} {string.Join(" ", extras)}";
    }

    /// <summary>
    /// Resuelve y ordena los días de entrenamiento semanales a partir de la lista preferida o valores por defecto (#158 · KAN-563).
    /// </summary>
    public static List<DayOfWeek> ResolveTrainingDays(IReadOnlyList<string>? trainingDays, int daysPerWeek)
    {
        var result = new List<DayOfWeek>();
        if (trainingDays is { Count: > 0 })
        {
            foreach (var rawDay in trainingDays)
            {
                if (TryParseDayOfWeek(rawDay, out var parsedDay) && !result.Contains(parsedDay))
                {
                    result.Add(parsedDay);
                }
            }
        }

        DayOfWeek[] defaultDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday];

        if (result.Count == 0)
        {
            result = defaultDays.Take(Math.Min(daysPerWeek, defaultDays.Length)).ToList();
        }
        else if (result.Count < daysPerWeek)
        {
            foreach (var def in defaultDays)
            {
                if (!result.Contains(def))
                {
                    result.Add(def);
                    if (result.Count >= daysPerWeek)
                        break;
                }
            }
        }
        else if (result.Count > daysPerWeek)
        {
            result = result.Take(daysPerWeek).ToList();
        }

        return result.OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d).ToList();
    }

    /// <summary>
    /// Intenta parsear un día de la semana a partir de nombres en español o inglés (#158 · KAN-563).
    /// </summary>
    public static bool TryParseDayOfWeek(string rawDay, out DayOfWeek dayOfWeek)
    {
        dayOfWeek = DayOfWeek.Monday;
        if (string.IsNullOrWhiteSpace(rawDay))
            return false;

        var clean = TextNormalizer.Normalize(rawDay);

        if (clean.StartsWith("lun") || clean.StartsWith("mon")) { dayOfWeek = DayOfWeek.Monday; return true; }
        if (clean.StartsWith("mar") || clean.StartsWith("tue")) { dayOfWeek = DayOfWeek.Tuesday; return true; }
        if (clean.StartsWith("mie") || clean.StartsWith("wed")) { dayOfWeek = DayOfWeek.Wednesday; return true; }
        if (clean.StartsWith("jue") || clean.StartsWith("thu")) { dayOfWeek = DayOfWeek.Thursday; return true; }
        if (clean.StartsWith("vie") || clean.StartsWith("fri")) { dayOfWeek = DayOfWeek.Friday; return true; }
        if (clean.StartsWith("sab") || clean.StartsWith("sat")) { dayOfWeek = DayOfWeek.Saturday; return true; }
        if (clean.StartsWith("dom") || clean.StartsWith("sun")) { dayOfWeek = DayOfWeek.Sunday; return true; }

        if (Enum.TryParse<DayOfWeek>(rawDay, ignoreCase: true, out var parsed))
        {
            dayOfWeek = parsed;
            return true;
        }

        return false;
    }

    public async Task<string?> TryGenerateDayAdjustmentJustificationAsync(
        IReadOnlyList<DayAdjustmentItemDto> items,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        var justification = DayAdjustmentMapper.BuildDeterministicJustification(items);
        stopwatch.Stop();

        var prompt = DayAdjustmentMapper.BuildJustificationPrompt(items);
        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: justification,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0), cancellationToken);

        return justification;
    }

    public async Task<TrainingRecommendationResponseDto?> TryGenerateTrainingRecommendationAsync(
        TrainingRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var recommendation = RecommendationPromptBuilder.BuildDeterministicTrainingRecommendation(request, ProviderName);
        var validation = RecommendationOutputValidator.ValidateTrainingDto(recommendation, request);
        stopwatch.Stop();

        var prompt = RecommendationPromptBuilder.BuildTrainingRecommendationPrompt(request);
        if (!validation.IsValid || validation.Value is null)
        {
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: validation.ValidationCode,
                athleteId: request.AthleteId,
                errorMessage: validation.ErrorMessage), cancellationToken);
            return null;
        }

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: validation.Value.RecommendationText,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0), cancellationToken);

        return validation.Value;
    }

    public async Task<NutritionRecommendationResponseDto?> TryGenerateNutritionRecommendationAsync(
        NutritionRecommendationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var recommendation = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request, ProviderName);
        var validation = RecommendationOutputValidator.ValidateNutritionDto(recommendation, request);
        stopwatch.Stop();

        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);
        if (!validation.IsValid || validation.Value is null)
        {
            await aiAuditService.LogAsync(AiLog.Create(
                ProviderName,
                prompt,
                stopwatch.ElapsedMilliseconds,
                isValid: false,
                validationResult: validation.ValidationCode,
                athleteId: request.AthleteId,
                errorMessage: validation.ErrorMessage), cancellationToken);
            return null;
        }

        await aiAuditService.LogAsync(AiLog.Create(
            ProviderName,
            prompt,
            stopwatch.ElapsedMilliseconds,
            isValid: true,
            validationResult: "Valido",
            response: validation.Value.SuggestedPlateComposition,
            athleteId: request.AthleteId,
            promptTokens: 0,
            completionTokens: 0,
            totalTokens: 0), cancellationToken);

        return validation.Value;
    }
}
