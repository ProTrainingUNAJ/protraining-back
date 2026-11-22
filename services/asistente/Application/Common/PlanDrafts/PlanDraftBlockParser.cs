namespace Application.Common.PlanDrafts;

using System.Text.Json;
using Application.Common.Ai;
using Application.Common.Helpers;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Infrastructure.Services.Ai.Rules;

/// <summary>
/// Parser y traductor del borrador de sesiones modelo por bloques con selección numérica.
/// Convierte los índices numéricos (1..N) de los bloques a GUIDs reales del catálogo,
/// ajusta determinísticamente la cantidad exacta de ejercicios pedida por el PF (recortando o completando
/// desde el catálogo filtrado con <see cref="RuleBasedSplitPlanner"/>) y proyecta las semanas 2 a N.
/// </summary>
public static class PlanDraftBlockParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parsea la respuesta estructurada por bloques de la IA y genera el borrador de planificación multisesión completo.
    /// Si <paramref name="maxExercisesPerSession"/> está definido, ajusta determinísticamente cada sesión a exactamente N
    /// ejercicios (podando si sobran o completando con el catálogo filtrado si faltan).
    /// </summary>
    public static (bool IsValid, PlanDraftContentDto? Content, string? ErrorMessage) ParseAndExpand(
        string rawResponse,
        IReadOnlyList<CatalogExerciseDto> catalog,
        int requestedWeeks,
        DateOnly? startDate = null,
        IReadOnlyList<DateOnly>? matchDates = null,
        int? maxExercisesPerSession = null,
        string? goal = null,
        AthleteAdaptationContextDto? adaptationContext = null)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return (false, null, "El texto recibido de la IA está vacío.");
        }

        if (catalog == null || catalog.Count == 0)
        {
            return (false, null, "El catálogo de ejercicios está vacío. No se pueden traducir los índices de bloques.");
        }

        var cleanedJson = AiRoutineParser.CleanMarkdownFences(rawResponse);

        try
        {
            // 1. Intentar deserializar como modelo estructurado por bloques funcionales
            var blockModel = JsonSerializer.Deserialize<BlockWeekModelDto>(cleanedJson, JsonOptions);
            if (blockModel?.ModelSessions is { Count: > 0 })
            {
                var effectiveGoal = !string.IsNullOrWhiteSpace(goal) ? goal : blockModel.Objective;
                var rawRequested = maxExercisesPerSession ?? GoalConstraintExtractor.ExtractRawRequestedExercisesPerSession(effectiveGoal);
                int? effectiveTarget = rawRequested.HasValue
                    ? Math.Clamp(rawRequested.Value, GoalConstraintExtractor.MinAllowedExercisesPerSession, GoalConstraintExtractor.MaxAllowedExercisesPerSession)
                    : null;

                var safeCatalog = FilterCatalogForRestrictions(catalog, adaptationContext);
                var modelSessions = new List<PlanDraftSessionDto>();

                for (var sessionIdx = 0; sessionIdx < blockModel.ModelSessions.Count; sessionIdx++)
                {
                    var blockSession = blockModel.ModelSessions[sessionIdx];
                    if (effectiveTarget is > 0)
                    {
                        blockSession = TrimBlockSessionToMaxExercises(blockSession, effectiveTarget.Value);
                    }

                    var dayOfWeek = ParseDayOfWeek(blockSession.DayOfWeek);
                    var sessionExercises = new List<PlanDraftExerciseDto>();
                    var currentOrder = 1;

                    // Aplanar los 4 bloques en la secuencia funcional oficial
                    if (AppendBlock(sessionExercises, blockSession.WarmUp, catalog, ref currentOrder, defaultSets: 2, defaultReps: 10, defaultPct: null) is { } errWarmUp)
                        return (false, null, errWarmUp);
                    if (AppendBlock(sessionExercises, blockSession.StrengthA, catalog, ref currentOrder, defaultSets: 4, defaultReps: 6, defaultPct: 80m) is { } errStrA)
                        return (false, null, errStrA);
                    if (AppendBlock(sessionExercises, blockSession.StrengthB, catalog, ref currentOrder, defaultSets: 3, defaultReps: 8, defaultPct: 75m) is { } errStrB)
                        return (false, null, errStrB);
                    if (AppendBlock(sessionExercises, blockSession.Accessories, catalog, ref currentOrder, defaultSets: 3, defaultReps: 12, defaultPct: null) is { } errAcc)
                        return (false, null, errAcc);

                    sessionExercises = RemoveOrReplaceRestrictedExercises(sessionExercises, safeCatalog, adaptationContext);

                    if (effectiveTarget is > 0)
                    {
                        if (sessionExercises.Count > effectiveTarget.Value)
                        {
                            sessionExercises = sessionExercises
                                .Take(effectiveTarget.Value)
                                .Select((ex, idx) => ex with { Order = idx + 1 })
                                .ToList();
                        }
                        else if (sessionExercises.Count < effectiveTarget.Value)
                        {
                            sessionExercises = CompleteSessionExercises(
                                sessionExercises,
                                sessionIdx,
                                blockModel.ModelSessions.Count,
                                effectiveTarget.Value,
                                safeCatalog,
                                effectiveGoal);
                        }
                    }

                    if (sessionExercises.Count == 0)
                    {
                        return (false, null, $"La sesión '{blockSession.Name}' no contiene ejercicios válidos en sus bloques.");
                    }

                    modelSessions.Add(new PlanDraftSessionDto(
                        Name: string.IsNullOrWhiteSpace(blockSession.Name) ? $"Sesión {sessionIdx + 1}" : blockSession.Name.Trim(),
                        DayOfWeek: dayOfWeek,
                        Order: blockSession.Order > 0 ? blockSession.Order : sessionIdx + 1,
                        CoachNotes: blockSession.CoachNotes,
                        Exercises: sessionExercises
                    ));
                }

                // 2. Proyectar las semanas 2 a N con el motor de progresión en C#
                var progressionWeeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
                    modelSessions,
                    requestedWeeks,
                    startDate,
                    matchDates);

                var content = new PlanDraftContentDto(
                    Name: string.IsNullOrWhiteSpace(blockModel.Name) ? "Planificación Deportiva por Bloques" : blockModel.Name.Trim(),
                    Objective: string.IsNullOrWhiteSpace(blockModel.Objective) ? "Desarrollo atlético integral" : blockModel.Objective.Trim(),
                    JustificationSummary: string.IsNullOrWhiteSpace(blockModel.JustificationSummary)
                        ? "Borrador armado por bloques funcionales con progresión determinística semanal."
                        : blockModel.JustificationSummary.Trim(),
                    Weeks: progressionWeeks
                );

                if (effectiveTarget is > 0)
                {
                    content = AppendExerciseCountNotes(content, effectiveTarget.Value, rawRequested);
                }

                return (true, content, null);
            }

            // 3. Fallback retrocompatible: si vino en el formato clásico PlanDraftContentDto directo
            var fallbackContent = PlanDraftMapper.DeserializeContent(cleanedJson);
            if (fallbackContent?.Weeks is { Count: > 0 })
            {
                var effectiveGoal = !string.IsNullOrWhiteSpace(goal) ? goal : fallbackContent.Objective;
                var rawRequested = maxExercisesPerSession ?? GoalConstraintExtractor.ExtractRawRequestedExercisesPerSession(effectiveGoal);
                int? effectiveTarget = rawRequested.HasValue
                    ? Math.Clamp(rawRequested.Value, GoalConstraintExtractor.MinAllowedExercisesPerSession, GoalConstraintExtractor.MaxAllowedExercisesPerSession)
                    : null;

                // Solo importa la primera semana del modelo: las demás las proyecta el motor de progresión
                fallbackContent = KeepFirstWeekAndProject(fallbackContent, requestedWeeks, startDate, matchDates);

                if (effectiveTarget is > 0)
                {
                    var safeCatalog = FilterCatalogForRestrictions(catalog, adaptationContext);
                    fallbackContent = EnforceExactExercisesPerSession(
                        fallbackContent,
                        effectiveTarget.Value,
                        rawRequested,
                        safeCatalog,
                        effectiveGoal,
                        startDate,
                        matchDates,
                        adaptationContext);
                }

                return (true, fallbackContent, null);
            }

            return (false, null, "La respuesta no contiene sesiones modelo por bloques ni semanas válidas.");
        }
        catch (JsonException ex)
        {
            return (false, null, $"Error de sintaxis JSON en la estructura por bloques: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Error al interpretar el borrador por bloques: {ex.Message}");
        }
    }

    /// <summary>
    /// Toma solo la primera semana que devolvió el modelo y proyecta las siguientes con el motor de progresión:
    /// la progresión la decide la regla en C#, no la IA (KAN-600).
    /// </summary>
    public static PlanDraftContentDto KeepFirstWeekAndProject(
        PlanDraftContentDto content,
        int requestedWeeks,
        DateOnly? startDate = null,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        var weeks = content.Weeks ?? [];
        if (weeks.Count == 0 || weeks[0].Sessions is not { Count: > 0 })
            return content;

        if (requestedWeeks <= 1)
        {
            return weeks.Count == 1
                ? content
                : content with { Weeks = [weeks[0] with { WeekNumber = 1 }] };
        }

        return content with
        {
            Weeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
                weeks[0].Sessions,
                requestedWeeks,
                startDate,
                matchDates)
        };
    }

    /// <summary>
    /// Garantiza determinísticamente que ninguna sesión supere el tope de ejercicios pedido por el preparador físico
    /// y, cuando se provee catálogo filtrado, completa los ejercicios faltantes hasta alcanzar exactamente la cantidad pedida (KAN-652).
    /// </summary>
    public static PlanDraftContentDto EnforceMaxExercisesPerSession(
        PlanDraftContentDto content,
        int maxExercisesPerSession) =>
        EnforceExactExercisesPerSession(content, maxExercisesPerSession);

    /// <summary>
    /// Ajusta determinísticamente todas las sesiones del borrador a exactamente <paramref name="targetExercisesPerSession"/>
    /// ejercicios (recortando si sobran o completando con <see cref="RuleBasedSplitPlanner"/> desde el catálogo filtrado si faltan),
    /// respetando restricciones por lesión y agregando aviso en la nota del borrador si pidieron más de 12 o si el catálogo no alcanza (KAN-652).
    /// </summary>
    public static PlanDraftContentDto EnforceExactExercisesPerSession(
        PlanDraftContentDto content,
        int targetExercisesPerSession,
        int? rawRequestedExercises = null,
        IReadOnlyList<CatalogExerciseDto>? catalog = null,
        string? goal = null,
        DateOnly? startDate = null,
        IReadOnlyList<DateOnly>? matchDates = null,
        AthleteAdaptationContextDto? adaptationContext = null)
    {
        if (content?.Weeks is not { Count: > 0 } || targetExercisesPerSession <= 0)
            return content!;

        var effectiveTarget = Math.Clamp(
            targetExercisesPerSession,
            GoalConstraintExtractor.MinAllowedExercisesPerSession,
            GoalConstraintExtractor.MaxAllowedExercisesPerSession);

        var effectiveRaw = rawRequestedExercises ?? (targetExercisesPerSession > GoalConstraintExtractor.MaxAllowedExercisesPerSession
            ? targetExercisesPerSession
            : null);

        var safeCatalog = catalog is { Count: > 0 }
            ? FilterCatalogForRestrictions(catalog, adaptationContext)
            : [];

        var effectiveGoal = !string.IsNullOrWhiteSpace(goal) ? goal : content.Objective;
        var hasCatalog = safeCatalog.Count > 0;
        var profile = RuleBasedGoalProfiles.Detect(effectiveGoal);

        var updatedWeeks = new List<PlanDraftWeekDto>(content.Weeks.Count);
        for (var weekIdx = 0; weekIdx < content.Weeks.Count; weekIdx++)
        {
            var week = content.Weeks[weekIdx];
            var sessions = week.Sessions ?? [];
            var updatedSessions = new List<PlanDraftSessionDto>(sessions.Count);

            for (var sessionIdx = 0; sessionIdx < sessions.Count; sessionIdx++)
            {
                var session = sessions[sessionIdx];
                var exercises = RemoveOrReplaceRestrictedExercises(session.Exercises ?? [], safeCatalog, adaptationContext);

                if (exercises.Count > effectiveTarget)
                {
                    exercises = exercises
                        .Take(effectiveTarget)
                        .Select((ex, idx) => ex with { Order = idx + 1 })
                        .ToList();
                }
                else if (exercises.Count < effectiveTarget && hasCatalog)
                {
                    exercises = CompleteSessionExercises(
                        exercises,
                        sessionIdx,
                        sessions.Count,
                        effectiveTarget,
                        safeCatalog,
                        effectiveGoal,
                        week.WeekNumber,
                        content.Weeks.Count,
                        profile);
                }

                updatedSessions.Add(session with { Exercises = exercises });
            }

            updatedWeeks.Add(week with { Sessions = updatedSessions });
        }

        var updatedContent = content with { Weeks = updatedWeeks };
        if (hasCatalog || effectiveRaw is > GoalConstraintExtractor.MaxAllowedExercisesPerSession)
        {
            updatedContent = AppendExerciseCountNotes(
                updatedContent,
                effectiveTarget,
                effectiveRaw,
                warnOnCatalogShortfall: hasCatalog);
        }

        return updatedContent;
    }

    public static IReadOnlyList<CatalogExerciseDto> FilterCatalogForRestrictions(
        IReadOnlyList<CatalogExerciseDto>? catalog,
        AthleteAdaptationContextDto? adaptationContext)
    {
        if (catalog is not { Count: > 0 })
            return [];

        if (adaptationContext?.Restrictions is not { Count: > 0 })
            return catalog;

        var restrictedIds = adaptationContext.Restrictions
            .Where(r => r.ExerciseId != Guid.Empty)
            .Select(r => r.ExerciseId)
            .ToHashSet();

        var safeList = catalog
            .Where(e => e.Id == Guid.Empty || !restrictedIds.Contains(e.Id))
            .ToList();

        var existingIds = safeList
            .Where(e => e.Id != Guid.Empty)
            .Select(e => e.Id)
            .ToHashSet();

        foreach (var substitute in adaptationContext.Restrictions.SelectMany(r => r.Substitutes))
        {
            if (substitute.ExerciseId == Guid.Empty ||
                restrictedIds.Contains(substitute.ExerciseId) ||
                !existingIds.Add(substitute.ExerciseId))
            {
                continue;
            }

            var fromCatalog = catalog.FirstOrDefault(e => e.Id == substitute.ExerciseId);
            safeList.Add(fromCatalog ?? new CatalogExerciseDto(substitute.ExerciseId, substitute.Name, null, null));
        }

        return safeList;
    }

    private static List<PlanDraftExerciseDto> RemoveOrReplaceRestrictedExercises(
        IReadOnlyList<PlanDraftExerciseDto> exercises,
        IReadOnlyList<CatalogExerciseDto> safeCatalog,
        AthleteAdaptationContextDto? adaptationContext)
    {
        if (exercises.Count == 0 || adaptationContext?.Restrictions is not { Count: > 0 })
            return exercises.ToList();

        var restrictionsById = adaptationContext.Restrictions
            .Where(r => r.ExerciseId != Guid.Empty)
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First());

        if (restrictionsById.Count == 0)
            return exercises.ToList();

        var usedIds = exercises
            .Where(e => e.ExerciseId.HasValue && e.ExerciseId.Value != Guid.Empty && !restrictionsById.ContainsKey(e.ExerciseId.Value))
            .Select(e => e.ExerciseId!.Value)
            .ToHashSet();

        var result = new List<PlanDraftExerciseDto>(exercises.Count);
        foreach (var ex in exercises)
        {
            if (!ex.ExerciseId.HasValue || !restrictionsById.TryGetValue(ex.ExerciseId.Value, out var restriction))
            {
                result.Add(ex with { Order = result.Count + 1 });
                continue;
            }

            var substitute = restriction.Substitutes
                .FirstOrDefault(s => s.ExerciseId != Guid.Empty && !restrictionsById.ContainsKey(s.ExerciseId) && usedIds.Add(s.ExerciseId));

            if (substitute is not null)
            {
                var catalogMatch = safeCatalog.FirstOrDefault(c => c.Id == substitute.ExerciseId);
                result.Add(ex with
                {
                    ExerciseId = substitute.ExerciseId,
                    ExerciseName = catalogMatch?.Name ?? substitute.Name,
                    MuscleGroup = catalogMatch?.MuscleGroup ?? ex.MuscleGroup,
                    IsReference = false,
                    Order = result.Count + 1
                });
            }
        }

        return result;
    }

    private static List<PlanDraftExerciseDto> CompleteSessionExercises(
        List<PlanDraftExerciseDto> currentExercises,
        int sessionIdx,
        int totalSessions,
        int targetCount,
        IReadOnlyList<CatalogExerciseDto> safeCatalog,
        string? goal,
        int weekNumber = 1,
        int totalWeeks = 1,
        RuleGoalProfile? predetectedProfile = null)
    {
        if (currentExercises.Count >= targetCount || safeCatalog.Count == 0)
            return currentExercises;

        var profile = predetectedProfile ?? RuleBasedGoalProfiles.Detect(goal);
        var daysPerWeek = Math.Max(1, totalSessions);
        var splitPlan = RuleBasedSplitPlanner.BuildSplit(
            safeCatalog,
            goal,
            daysPerWeek,
            Math.Max(targetCount, safeCatalog.Count));

        var candidates = new List<CatalogExerciseDto>();
        if (splitPlan.Sessions.Count > 0)
        {
            var blueprint = splitPlan.Sessions[sessionIdx % splitPlan.Sessions.Count];
            candidates.AddRange(blueprint.Exercises);
            candidates.AddRange(blueprint.FallbackPool);
        }

        // Si el objetivo no restringió a un único grupo muscular y el pool del día tiene menos ejercicios que targetCount,
        // completar con el pool general ordenado por compuestos primero (RuleBasedSplitPlanner de 1 día) y restantes del catálogo.
        var focusedGroups = GoalMuscleFocusMapper.ExtractMuscleGroups(goal);
        if (focusedGroups.Count == 0)
        {
            var fullBodySplit = RuleBasedSplitPlanner.BuildSplit(safeCatalog, null, 1, safeCatalog.Count);
            if (fullBodySplit.Sessions.Count > 0)
            {
                candidates.AddRange(fullBodySplit.Sessions[0].Exercises);
                candidates.AddRange(fullBodySplit.Sessions[0].FallbackPool);
            }
            candidates.AddRange(safeCatalog);
        }
        else if (candidates.Count == 0)
        {
            // Si el catálogo filtrado provisto no tiene MuscleGroup informado en sus ítems, usar safeCatalog directo
            candidates.AddRange(safeCatalog);
        }

        var result = new List<PlanDraftExerciseDto>(currentExercises);
        var usedIds = result
            .Where(e => e.ExerciseId.HasValue && e.ExerciseId.Value != Guid.Empty)
            .Select(e => e.ExerciseId!.Value)
            .ToHashSet();

        var usedNames = result
            .Where(e => !string.IsNullOrWhiteSpace(e.ExerciseName))
            .Select(e => TextNormalizer.Normalize(e.ExerciseName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isDeloadWeek = totalWeeks >= 4 && weekNumber == totalWeeks;

        foreach (var candidate in candidates)
        {
            if (result.Count >= targetCount)
                break;

            var hasValidId = candidate.Id != Guid.Empty;
            var normName = TextNormalizer.Normalize(candidate.Name);

            if (hasValidId && usedIds.Contains(candidate.Id))
                continue;

            if (!string.IsNullOrEmpty(normName) && usedNames.Contains(normName))
                continue;

            if (hasValidId)
                usedIds.Add(candidate.Id);
            if (!string.IsNullOrEmpty(normName))
                usedNames.Add(normName);

            var nextOrder = result.Count + 1;
            var prescription = RuleBasedGoalProfiles.GetPrescription(profile, nextOrder);
            var sets = prescription.Sets;
            var reps = prescription.Reps;
            var pct = prescription.BasePercentage;

            if (weekNumber > 1)
            {
                if (prescription.IsMain && !isDeloadWeek)
                {
                    pct = Math.Min(
                        prescription.BasePercentage + ((weekNumber - 1) * 2.5m),
                        prescription.MaxPercentage);
                }

                if (isDeloadWeek)
                {
                    sets = Math.Max(1, sets - 1);
                    pct = Math.Max(40.0m, pct - 10.0m);
                }
            }

            result.Add(new PlanDraftExerciseDto(
                ExerciseId: hasValidId ? candidate.Id : null,
                Order: nextOrder,
                Sets: sets,
                Reps: reps,
                TargetPercentage: pct,
                ExerciseName: candidate.Name,
                MuscleGroup: candidate.MuscleGroup,
                IsReference: !hasValidId));
        }

        return result
            .Select((ex, idx) => ex with { Order = idx + 1 })
            .ToList();
    }

    public static PlanDraftContentDto AppendExerciseCountNotes(
        PlanDraftContentDto content,
        int targetExercisesPerSession,
        int? rawRequestedExercises = null,
        bool warnOnCatalogShortfall = true)
    {
        if (content?.Weeks is not { Count: > 0 })
            return content!;

        var notesToAdd = new List<string>();

        if (rawRequestedExercises is > GoalConstraintExtractor.MaxAllowedExercisesPerSession)
        {
            notesToAdd.Add(
                $"Nota: se pidieron {rawRequestedExercises.Value} ejercicios por sesión, pero el máximo permitido es de {GoalConstraintExtractor.MaxAllowedExercisesPerSession} ejercicios por sesión.");
        }

        if (warnOnCatalogShortfall)
        {
            var allSessions = content.Weeks.SelectMany(w => w.Sessions ?? []).ToList();
            if (allSessions.Count > 0)
            {
                var minCount = allSessions.Min(s => s.Exercises?.Count ?? 0);
                if (minCount < targetExercisesPerSession)
                {
                    notesToAdd.Add(
                        $"Nota: se pidieron {targetExercisesPerSession} ejercicios por sesión, pero el catálogo filtrado solo cuenta con {minCount} ejercicios compatibles.");
                }
            }
        }

        if (notesToAdd.Count == 0)
            return content;

        var summary = content.JustificationSummary ?? string.Empty;
        foreach (var note in notesToAdd)
        {
            if (!summary.Contains(note, StringComparison.OrdinalIgnoreCase))
            {
                summary = string.IsNullOrWhiteSpace(summary) ? note : $"{summary.TrimEnd()} {note}";
            }
        }

        var updatedWeeks = content.Weeks.Select(w => w with
        {
            Sessions = (w.Sessions ?? []).Select(s =>
            {
                var coachNotes = s.CoachNotes ?? string.Empty;
                foreach (var note in notesToAdd)
                {
                    if (!coachNotes.Contains(note, StringComparison.OrdinalIgnoreCase))
                    {
                        coachNotes = string.IsNullOrWhiteSpace(coachNotes) ? note : $"{coachNotes.TrimEnd()} {note}";
                    }
                }
                return s with { CoachNotes = coachNotes };
            }).ToList()
        }).ToList();

        return content with
        {
            JustificationSummary = summary,
            Weeks = updatedWeeks
        };
    }

    /// <summary>
    /// Poda una sesión por bloques cuando el modelo devolvió más ejercicios de los que pidió el PF,
    /// preservando los núcleos principales de fuerza (strengthA y strengthB) y al menos 1 ejercicio de entrada en calor.
    /// </summary>
    private static BlockSessionModelDto TrimBlockSessionToMaxExercises(
        BlockSessionModelDto session,
        int maxExercises)
    {
        if (maxExercises <= 0)
            return session;

        var warmUp = new List<BlockExerciseItemDto>(session.WarmUp ?? []);
        var strengthA = new List<BlockExerciseItemDto>(session.StrengthA ?? []);
        var strengthB = new List<BlockExerciseItemDto>(session.StrengthB ?? []);
        var accessories = new List<BlockExerciseItemDto>(session.Accessories ?? []);

        int TotalCount() => warmUp.Count + strengthA.Count + strengthB.Count + accessories.Count;

        if (TotalCount() <= maxExercises)
            return session;

        // 1. Si hay más de 2 accesorios, recortar primero los accesorios excedentes
        while (TotalCount() > maxExercises && accessories.Count > 2)
            accessories.RemoveAt(accessories.Count - 1);

        // 2. Si hay más de 1 entrada en calor, dejar 1 sola entrada en calor
        while (TotalCount() > maxExercises && warmUp.Count > 1)
            warmUp.RemoveAt(warmUp.Count - 1);

        // 3. Si aún sobra, recortar accesorios restantes (de atrás hacia adelante)
        while (TotalCount() > maxExercises && accessories.Count > 0)
            accessories.RemoveAt(accessories.Count - 1);

        // 4. Si aún sobra, recortar excedentes de strengthB (> 1)
        while (TotalCount() > maxExercises && strengthB.Count > 1)
            strengthB.RemoveAt(strengthB.Count - 1);

        // 5. Si el tope es muy bajo (<= 2), recortar warmUp
        while (TotalCount() > maxExercises && warmUp.Count > 0)
            warmUp.RemoveAt(warmUp.Count - 1);

        // 6. Si aún sobra, recortar excedentes de strengthA (> 1) y luego strengthB
        while (TotalCount() > maxExercises && strengthA.Count > 1)
            strengthA.RemoveAt(strengthA.Count - 1);

        while (TotalCount() > maxExercises && strengthB.Count > 0)
            strengthB.RemoveAt(strengthB.Count - 1);

        return session with
        {
            WarmUp = warmUp,
            StrengthA = strengthA,
            StrengthB = strengthB,
            Accessories = accessories
        };
    }

    private static string? AppendBlock(
        List<PlanDraftExerciseDto> targetList,
        List<BlockExerciseItemDto>? blockItems,
        IReadOnlyList<CatalogExerciseDto> catalog,
        ref int currentOrder,
        int defaultSets,
        int defaultReps,
        decimal? defaultPct)
    {
        if (blockItems == null || blockItems.Count == 0)
            return null;

        foreach (var item in blockItems)
        {
            var sets = item.Sets > 0 ? item.Sets : defaultSets;
            var reps = item.Reps > 0 ? item.Reps : defaultReps;
            var pct = item.TargetPercentage ?? defaultPct;

            // Si viene un índice válido (1..N), resolvemos directo del catálogo
            var byIndex = item.ExerciseIndex is > 0
                ? ResolveCatalogExercise(item.ExerciseIndex.Value, catalog)
                : null;
            if (byIndex != null)
            {
                targetList.Add(new PlanDraftExerciseDto(
                    ExerciseId: byIndex.Id,
                    Order: currentOrder++,
                    Sets: sets,
                    Reps: reps,
                    TargetPercentage: pct,
                    ExerciseName: byIndex.Name,
                    IsReference: false,
                    MuscleGroup: byIndex.MuscleGroup
                ));
                continue;
            }

            // Si no vino un índice válido, buscamos por nombre (por si ya existe en el catálogo)
            if (!string.IsNullOrWhiteSpace(item.ExerciseName))
            {
                var normalizedTarget = TextNormalizer.Normalize(item.ExerciseName);
                var byName = catalog.FirstOrDefault(c =>
                    TextNormalizer.Normalize(c.Name) == normalizedTarget);
                if (byName != null)
                {
                    targetList.Add(new PlanDraftExerciseDto(
                        ExerciseId: byName.Id,
                        Order: currentOrder++,
                        Sets: sets,
                        Reps: reps,
                        TargetPercentage: pct,
                        ExerciseName: byName.Name,
                        IsReference: false,
                        MuscleGroup: byName.MuscleGroup
                    ));
                    continue;
                }

                // Si no existe en el catálogo -> se trae como ejercicio de referencia
                targetList.Add(new PlanDraftExerciseDto(
                    ExerciseId: null,
                    Order: currentOrder++,
                    Sets: sets,
                    Reps: reps,
                    TargetPercentage: pct,
                    ExerciseName: item.ExerciseName.Trim(),
                    IsReference: true,
                    MuscleGroup: string.IsNullOrWhiteSpace(item.MuscleGroup) ? null : item.MuscleGroup.Trim()
                ));
                continue;
            }

            // Fallback si vino índice 0 sin exerciseName (retrocompatibilidad)
            var zeroFallback = item.ExerciseIndex.HasValue
                ? ResolveCatalogExercise(item.ExerciseIndex.Value, catalog)
                : null;
            if (zeroFallback == null)
            {
                return $"El ejercicio con índice {item.ExerciseIndex} no existe en el catálogo disponible (1..{catalog.Count}) y no trajo 'exerciseName'.";
            }
            targetList.Add(new PlanDraftExerciseDto(
                ExerciseId: zeroFallback.Id,
                Order: currentOrder++,
                Sets: sets,
                Reps: reps,
                TargetPercentage: pct,
                ExerciseName: zeroFallback.Name,
                IsReference: false,
                MuscleGroup: zeroFallback.MuscleGroup
            ));
        }

        return null;
    }

    private static CatalogExerciseDto? ResolveCatalogExercise(int index, IReadOnlyList<CatalogExerciseDto> catalog)
    {
        // El modelo devuelve índice basado en 1 (1..catalog.Count)
        if (index >= 1 && index <= catalog.Count)
        {
            return catalog[index - 1];
        }

        // Si devuelve índice basado en 0 por error
        if (index >= 0 && index < catalog.Count)
        {
            return catalog[index];
        }

        return null;
    }

    private static DayOfWeek ParseDayOfWeek(string? rawDay)
    {
        if (string.IsNullOrWhiteSpace(rawDay))
            return DayOfWeek.Monday;

        var clean = TextNormalizer.Normalize(rawDay);

        if (clean.StartsWith("lun") || clean.StartsWith("mon")) return DayOfWeek.Monday;
        if (clean.StartsWith("mar") || clean.StartsWith("tue")) return DayOfWeek.Tuesday;
        if (clean.StartsWith("mie") || clean.StartsWith("wed")) return DayOfWeek.Wednesday;
        if (clean.StartsWith("jue") || clean.StartsWith("thu")) return DayOfWeek.Thursday;
        if (clean.StartsWith("vie") || clean.StartsWith("fri")) return DayOfWeek.Friday;
        if (clean.StartsWith("sab") || clean.StartsWith("sat")) return DayOfWeek.Saturday;
        if (clean.StartsWith("dom") || clean.StartsWith("sun")) return DayOfWeek.Sunday;

        if (Enum.TryParse<DayOfWeek>(rawDay, ignoreCase: true, out var parsed))
            return parsed;

        return DayOfWeek.Monday;
    }
}
