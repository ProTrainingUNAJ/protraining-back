namespace Application.Common.Ai;

using Application.Common.Adaptation;
using Application.Common.Helpers;
using Application.DTOs.Ai;
using Application.DTOs.Adaptation;
using Application.Interfaces.Services;

/// <summary>
/// Implementación del validador semántico en C# para verificar la existencia real de los ejercicios y ejercicioId.
/// Detecta IDs inventados o inexistentes, los corrige contra el catálogo oficial y conmuta de manera segura
/// al motor determinístico cuando la propuesta no cumple los criterios de fiabilidad.
/// </summary>
public sealed class ExerciseSemanticValidator(
    IExerciseCatalogService catalogService) : IExerciseSemanticValidator
{
    public async Task<RoutineSemanticValidationResult> ValidateDraftAsync(
        RoutineSuggestionResponseDto draft,
        SemanticValidationPolicy? policy = null,
        AthleteAdaptationContextDto? adaptationContext = null,
        CancellationToken cancellationToken = default)
    {
        policy ??= SemanticValidationPolicy.Default;

        if (draft == null || draft.Exercises == null || draft.Exercises.Count == 0)
        {
            return new RoutineSemanticValidationResult(
                IsValid: false,
                RequiresDeterministicFallback: true,
                HasCorrections: false,
                ExerciseResults: [],
                SanitizedRoutine: draft ?? new RoutineSuggestionResponseDto(Guid.Empty, string.Empty, string.Empty, [], "None"),
                ValidationMessages: ["La propuesta de rutina no contiene ejercicios para validar."]
            );
        }

        var itemResults = new List<ExerciseSemanticValidationItemResult>();
        var sanitizedExercises = new List<SuggestedExerciseDto>();
        var messages = new List<string>();
        var hasCorrections = false;

        foreach (var exercise in draft.Exercises)
        {
            var (sanitized, status, message) = await ValidateSingleExerciseAsync(exercise, policy, cancellationToken);

            if (status == ExerciseValidationStatus.IdCorrected)
            {
                hasCorrections = true;
            }

            itemResults.Add(new ExerciseSemanticValidationItemResult(exercise, sanitized, status, message));
            sanitizedExercises.Add(sanitized);
            messages.Add(message);
        }

        var validOrCorrectedCount = itemResults.Count(r => r.Status is ExerciseValidationStatus.Valid or ExerciseValidationStatus.IdCorrected);
        var referenceCount = itemResults.Count(r => r.Status == ExerciseValidationStatus.BroughtAsReference);
        var uncatalogedCount = itemResults.Count(r => r.Status == ExerciseValidationStatus.Uncataloged);

        var requiresFallback = false;

        if (uncatalogedCount > policy.MaxAllowedUncataloged)
        {
            requiresFallback = true;
            messages.Add($"La propuesta contiene {uncatalogedCount} ejercicios no identificados (máximo permitido: {policy.MaxAllowedUncataloged}). Requiere conmutación determinística.");
        }

        if (policy.RequireAtLeastOneCatalogExercise && validOrCorrectedCount == 0)
        {
            requiresFallback = true;
            messages.Add("Ningún ejercicio propuesto existe en el catálogo del club. Requiere conmutación determinística segura.");
        }

        if (!policy.AllowReferenceExercises && referenceCount > 0)
        {
            requiresFallback = true;
            messages.Add($"La política no permite ejercicios fuera de catálogo y se detectaron {referenceCount} referencias.");
        }

        var sanitizedRoutine = draft with
        {
            Exercises = sanitizedExercises
        };

        if (adaptationContext is not null)
        {
            var initialItemResults = itemResults.ToList();
            var adaptation = await ApplyAuthorizedAdaptationsAsync(
                sanitizedRoutine,
                adaptationContext,
                initialItemResults,
                messages,
                itemResults,
                cancellationToken);
            sanitizedRoutine = adaptation.Routine;
            hasCorrections |= adaptation.HasCorrections;

            var activeRestrictionCount = adaptationContext.Restrictions
                .Where(r => r.Substitutes.Count > 0)
                .Select(r => r.ExerciseId)
                .Distinct()
                .Count();
            var maxAllowedHallucinatedAdaptations = Math.Max(policy.MaxAllowedUncataloged, activeRestrictionCount);

            if (adaptation.HasCorrections &&
                uncatalogedCount <= policy.MaxAllowedUncataloged &&
                (policy.AllowReferenceExercises || referenceCount == 0) &&
                (validOrCorrectedCount > 0 || referenceCount <= maxAllowedHallucinatedAdaptations) &&
                sanitizedRoutine.Exercises.Count > 0 &&
                sanitizedRoutine.Exercises.All(e => e.ExerciseId.HasValue && e.ExerciseId.Value != Guid.Empty && !e.IsReference))
            {
                requiresFallback = false;
            }
        }

        return new RoutineSemanticValidationResult(
            IsValid: !requiresFallback,
            RequiresDeterministicFallback: requiresFallback,
            HasCorrections: hasCorrections,
            ExerciseResults: itemResults,
            SanitizedRoutine: sanitizedRoutine,
            ValidationMessages: messages
        );
    }

    private async Task<AdaptationValidationResult> ApplyAuthorizedAdaptationsAsync(
        RoutineSuggestionResponseDto routine,
        AthleteAdaptationContextDto context,
        IReadOnlyList<ExerciseSemanticValidationItemResult> initialItemResults,
        ICollection<string> messages,
        ICollection<ExerciseSemanticValidationItemResult> itemResults,
        CancellationToken cancellationToken)
    {
        var hasExplicitBaseExercises = routine.BaseExercises is not null;
        var baseExercises = routine.BaseExercises ?? routine.Exercises.ToList();

        if (context.Restrictions.Count == 0)
            return new(routine with { BaseExercises = baseExercises }, false);

        var restrictionsByExercise = AuthorizedSubstituteHelper.BuildRestrictionsByExercise(context);

        if (restrictionsByExercise.Count == 0)
            return new(routine with { BaseExercises = baseExercises }, false);

        var satisfiedRestrictionIds = new HashSet<Guid>();
        var adaptedIndices = new HashSet<int>();
        var exercises = new List<SuggestedExerciseDto>(routine.Exercises.Count);
        var changes = new List<AdaptationChangeDto>();
        var adaptedAny = false;

        // Pasada 1: Violaciones directas a ejercicios restringidos y control contra BaseExercises explícito
        for (var i = 0; i < routine.Exercises.Count; i++)
        {
            var exercise = routine.Exercises[i];
            if (exercise.ExerciseId is { } exerciseId &&
                restrictionsByExercise.TryGetValue(exerciseId, out var restriction))
            {
                var substitute = restriction.Substitutes[0];
                var message = AuthorizedSubstituteHelper.BuildAdaptationReason(restriction, substitute.Name);
                exercises.Add(RecordSubstituteAdaptation(
                    exercise,
                    exercise,
                    restriction,
                    substitute,
                    message,
                    messages,
                    changes,
                    itemResults));
                adaptedAny = true;
                adaptedIndices.Add(i);
                satisfiedRestrictionIds.Add(restriction.ExerciseId);
                continue;
            }

            if (hasExplicitBaseExercises &&
                i < routine.BaseExercises!.Count &&
                routine.BaseExercises[i].ExerciseId is { } baseExerciseId &&
                restrictionsByExercise.TryGetValue(baseExerciseId, out var baseRestriction))
            {
                var authorizedIds = baseRestriction.Substitutes.Select(s => s.ExerciseId).ToHashSet();
                if (exercise.ExerciseId is { } proposedId && !exercise.IsReference && authorizedIds.Contains(proposedId))
                {
                    exercises.Add(exercise);
                    satisfiedRestrictionIds.Add(baseRestriction.ExerciseId);
                    continue;
                }

                var substitute = baseRestriction.Substitutes[0];
                var message = AuthorizedSubstituteHelper.BuildUnauthorizedSubstituteReason(baseRestriction, exercise.ExerciseName, substitute.Name);
                exercises.Add(RecordSubstituteAdaptation(
                    exercise,
                    routine.BaseExercises[i],
                    baseRestriction,
                    substitute,
                    message,
                    messages,
                    changes,
                    itemResults));
                adaptedAny = true;
                adaptedIndices.Add(i);
                satisfiedRestrictionIds.Add(baseRestriction.ExerciseId);
                continue;
            }

            exercises.Add(exercise);
        }

        // Marcar como satisfechas las restricciones cuyos sustitutos autorizados ya están presentes como válidos
        var presentValidExerciseIds = CollectValidCatalogExerciseIds(exercises);
        foreach (var restriction in restrictionsByExercise.Values)
        {
            if (restriction.Substitutes.Any(s => presentValidExerciseIds.Contains(s.ExerciseId)))
            {
                satisfiedRestrictionIds.Add(restriction.ExerciseId);
            }
        }

        // Pasada 2: Alucinaciones / ejercicios fuera de catálogo únicamente cuando coinciden con el mismo
        // grupo muscular o zona de una restricción pendiente y hay un sustituto libre sin duplicar (KAN-466)
        Dictionary<Guid, CatalogExerciseDto>? restrictedCatalogCache = null;
        for (var i = 0; i < exercises.Count; i++)
        {
            if (adaptedIndices.Contains(i))
                continue;

            var current = exercises[i];
            var isHallucinated = current.IsReference ||
                !current.ExerciseId.HasValue ||
                current.ExerciseId.Value == Guid.Empty ||
                (i < initialItemResults.Count &&
                 initialItemResults[i].Status is ExerciseValidationStatus.BroughtAsReference or ExerciseValidationStatus.Uncataloged);

            if (!isHallucinated)
                continue;

            var pendingRestrictions = restrictionsByExercise.Values
                .Where(r => !satisfiedRestrictionIds.Contains(r.ExerciseId))
                .ToList();

            if (pendingRestrictions.Count == 0)
                continue;

            restrictedCatalogCache ??= await LoadRestrictedCatalogItemsAsync(restrictionsByExercise.Keys, cancellationToken);
            var originalHallucinated = i < initialItemResults.Count ? initialItemResults[i].OriginalExercise : current;

            var applicableRestriction = pendingRestrictions.FirstOrDefault(r =>
                MatchesRestrictionMuscleGroup(originalHallucinated.ExerciseName, r, restrictedCatalogCache));

            if (applicableRestriction is null)
                continue;

            var presentIds = CollectValidCatalogExerciseIds(exercises);
            var substitute = applicableRestriction.Substitutes
                .FirstOrDefault(s => !presentIds.Contains(s.ExerciseId));

            // Nunca duplicar un ejercicio que ya está presente en la sesión
            if (substitute is null)
                continue;

            var message = AuthorizedSubstituteHelper.BuildUnauthorizedSubstituteReason(applicableRestriction, originalHallucinated.ExerciseName, substitute.Name);

            exercises[i] = RecordSubstituteAdaptation(
                originalHallucinated,
                originalHallucinated,
                applicableRestriction,
                substitute,
                message,
                messages,
                changes,
                itemResults);
            adaptedAny = true;
            adaptedIndices.Add(i);
            satisfiedRestrictionIds.Add(applicableRestriction.ExerciseId);
        }

        // Pasada 3: Sustituto real del catálogo pero NO autorizado en sesión de 1 ejercicio (KAN-462)
        if (!adaptedAny && exercises.Count == 1 && restrictionsByExercise.Count >= 1)
        {
            var proposed = exercises[0];
            var allAuthorizedIds = restrictionsByExercise.Values
                .SelectMany(r => r.Substitutes)
                .Select(s => s.ExerciseId)
                .ToHashSet();

            if (proposed.ExerciseId is not { } proposedId || !allAuthorizedIds.Contains(proposedId))
            {
                var restriction = restrictionsByExercise.Values.First();
                var substitute = restriction.Substitutes[0];
                var message = AuthorizedSubstituteHelper.BuildUnauthorizedSubstituteReason(restriction, proposed.ExerciseName, substitute.Name);
                exercises[0] = RecordSubstituteAdaptation(
                    proposed,
                    proposed,
                    restriction,
                    substitute,
                    message,
                    messages,
                    changes,
                    itemResults);
                adaptedAny = true;
            }
        }

        return new(
            routine with
        {
            Exercises = exercises,
            BaseExercises = baseExercises,
            WasAdapted = adaptedAny,
            AdaptationWarnings = messages.Where(message => message.Contains("sustituto autorizado", StringComparison.OrdinalIgnoreCase)).ToList(),
            AdaptationChanges = changes
        },
            adaptedAny);
    }

    private async Task<Dictionary<Guid, CatalogExerciseDto>> LoadRestrictedCatalogItemsAsync(
        IEnumerable<Guid> restrictedExerciseIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, CatalogExerciseDto>();
        foreach (var id in restrictedExerciseIds)
        {
            if (id == Guid.Empty || map.ContainsKey(id))
                continue;

            var item = await catalogService.FindByIdAsync(id, cancellationToken);
            if (item is not null)
                map[id] = item;
        }

        return map;
    }

    private static bool MatchesRestrictionMuscleGroup(
        string? hallucinatedName,
        ExerciseRestrictionContextDto restriction,
        IReadOnlyDictionary<Guid, CatalogExerciseDto> restrictedCatalogItems)
    {
        if (string.IsNullOrWhiteSpace(hallucinatedName))
            return false;

        var hallucinatedGroups = ExtractExerciseMuscleGroups(hallucinatedName);
        if (hallucinatedGroups.Count == 0)
            return false;

        var restrictionGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (restrictedCatalogItems.TryGetValue(restriction.ExerciseId, out var catalogItem) &&
            !string.IsNullOrWhiteSpace(catalogItem.MuscleGroup))
        {
            foreach (var group in ExtractExerciseMuscleGroups(catalogItem.MuscleGroup))
                restrictionGroups.Add(group);
            restrictionGroups.Add(catalogItem.MuscleGroup.Trim());
        }

        foreach (var zoneGroup in MapZoneToMuscleGroups(restriction.Zone))
            restrictionGroups.Add(zoneGroup);

        return hallucinatedGroups.Any(restrictionGroups.Contains);
    }

    private static HashSet<string> ExtractExerciseMuscleGroups(string text)
    {
        var groups = new HashSet<string>(
            GoalMuscleFocusMapper.ExtractMuscleGroups(text),
            StringComparer.OrdinalIgnoreCase);

        var norm = TextNormalizer.Normalize(text);
        if (norm.Contains("salto") || norm.Contains("jump") || norm.Contains("pliometr") || norm.Contains("rodilla"))
            groups.Add("Piernas");

        return groups;
    }

    private static IReadOnlyList<string> MapZoneToMuscleGroups(string? zone)
    {
        var norm = TextNormalizer.Normalize(zone);
        if (string.IsNullOrEmpty(norm))
            return [];

        if (norm.Contains("rodilla") || norm.Contains("knee") || norm.Contains("cuadriceps") ||
            norm.Contains("isquio") || norm.Contains("gluteo") || norm.Contains("gemelo") || norm.Contains("tobillo") ||
            norm.Contains("pierna") || norm.Contains("cadera"))
        {
            return ["Piernas"];
        }

        if (norm.Contains("hombro") || norm.Contains("shoulder"))
            return ["Hombros", "Pecho"];

        if (norm.Contains("pecho") || norm.Contains("pectoral"))
            return ["Pecho"];

        if (norm.Contains("espalda") || norm.Contains("lumbar") || norm.Contains("dorsal") || norm.Contains("back"))
            return ["Espalda", "Piernas"];

        if (norm.Contains("codo") || norm.Contains("muneca") || norm.Contains("biceps") || norm.Contains("triceps") || norm.Contains("brazo"))
            return ["Brazos"];

        if (norm.Contains("core") || norm.Contains("abdom"))
            return ["Core"];

        return [];
    }

    private static HashSet<Guid> CollectValidCatalogExerciseIds(IEnumerable<SuggestedExerciseDto> exercises) =>
        exercises
            .Where(e => !e.IsReference && e.ExerciseId.HasValue && e.ExerciseId.Value != Guid.Empty)
            .Select(e => e.ExerciseId!.Value)
            .ToHashSet();

    private static SuggestedExerciseDto RecordSubstituteAdaptation(
        SuggestedExerciseDto targetExercise,
        SuggestedExerciseDto originalForChange,
        ExerciseRestrictionContextDto restriction,
        AuthorizedSubstituteDto substitute,
        string message,
        ICollection<string> messages,
        ICollection<AdaptationChangeDto> changes,
        ICollection<ExerciseSemanticValidationItemResult> itemResults)
    {
        var adapted = AuthorizedSubstituteHelper.CreateAdaptedExercise(targetExercise, substitute);

        messages.Add(message);
        changes.Add(AuthorizedSubstituteHelper.CreateAdaptationChange(originalForChange, restriction, substitute, message));
        itemResults.Add(new ExerciseSemanticValidationItemResult(
            targetExercise,
            adapted,
            ExerciseValidationStatus.AdaptedToAuthorizedSubstitute,
            message));

        return adapted;
    }

    private sealed record AdaptationValidationResult(
        RoutineSuggestionResponseDto Routine,
        bool HasCorrections);

    public async Task<RoutineSuggestionResponseDto> ValidateOrFallbackAsync(
        RoutineSuggestionResponseDto draft,
        RoutineSuggestionRequestDto originalRequest,
        SemanticValidationPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateDraftAsync(draft, policy, cancellationToken: cancellationToken);

        if (!validation.RequiresDeterministicFallback)
        {
            return validation.SanitizedRoutine;
        }

        // Conmutación a generación determinística segura
        return await GenerateSafeDeterministicProposalAsync(originalRequest, cancellationToken);
    }

    private async Task<(SuggestedExerciseDto Sanitized, ExerciseValidationStatus Status, string Message)> ValidateSingleExerciseAsync(
        SuggestedExerciseDto exercise,
        SemanticValidationPolicy policy,
        CancellationToken cancellationToken)
    {
        // 1. Caso: El ejercicio tiene ejercicioId provisto
        if (exercise.ExerciseId.HasValue && exercise.ExerciseId.Value != Guid.Empty)
        {
            var byId = await catalogService.FindByIdAsync(exercise.ExerciseId.Value, cancellationToken);
            if (byId != null)
            {
                // El ID existe en el catálogo oficial
                return (
                    exercise with { ExerciseId = byId.Id, IsReference = false },
                    ExerciseValidationStatus.Valid,
                    $"Ejercicio '{exercise.ExerciseName}' verificado con ejercicioId válido '{byId.Id}'."
                );
            }

            var byName = await catalogService.FindByNameAsync(exercise.ExerciseName, cancellationToken);
            if (byName != null)
            {
                if (exercise.ExerciseId == byName.Id)
                {
                    // Si el identificador ya coincide exactamente con el del catálogo oficial, es válido (sin corrección requerida)
                    return (
                        exercise with { ExerciseId = byName.Id, IsReference = false },
                        ExerciseValidationStatus.Valid,
                        $"Ejercicio '{exercise.ExerciseName}' verificado con ejercicioId válido '{byName.Id}'."
                    );
                }

                // Detección y corrección: se vincula el identificador oficial correspondiente al ejercicio identificado
                return (
                    exercise with { ExerciseId = byName.Id, IsReference = false },
                    ExerciseValidationStatus.IdCorrected,
                    $"Detectado identificador no registrado '{exercise.ExerciseId}' en '{exercise.ExerciseName}'. Se vinculó al ID oficial '{byName.Id}' del catálogo ('{byName.Name}')."
                );
            }

            // El ejercicio no fue encontrado en el catálogo oficial
            if (policy.AllowReferenceExercises)
            {
                // Ajuste N2: incorporar como ejercicio de referencia en lugar de descartar la propuesta
                return (
                    exercise with { ExerciseId = null, IsReference = true },
                    ExerciseValidationStatus.BroughtAsReference,
                    $"El ejercicio '{exercise.ExerciseName}' no está registrado en el catálogo. Se incluye como referencia (N2) con 'esReferencia = true' y 'ejercicioId = null'."
                );
            }

            return (
                exercise,
                ExerciseValidationStatus.Uncataloged,
                $"El ejercicio '{exercise.ExerciseName}' con identificador '{exercise.ExerciseId}' no existe en el catálogo oficial."
            );
        }

        // 2. Caso: ejercicioId no fue provisto (es nulo o Guid.Empty)
        var itemByName = await catalogService.FindByNameAsync(exercise.ExerciseName, cancellationToken);
        if (itemByName != null)
        {
            // Se asocia el identificador oficial correspondiente al nombre identificado en el catálogo
            return (
                exercise with { ExerciseId = itemByName.Id, IsReference = false },
                ExerciseValidationStatus.IdCorrected,
                $"Ejercicio '{exercise.ExerciseName}' sin identificador: se asignó el ejercicioId oficial '{itemByName.Id}' del catálogo ('{itemByName.Name}')."
            );
        }

        if (policy.AllowReferenceExercises)
        {
            // Ajuste N2: incorporar como referencia para evaluación del preparador físico
            return (
                exercise with { ExerciseId = null, IsReference = true },
                ExerciseValidationStatus.BroughtAsReference,
                $"El ejercicio '{exercise.ExerciseName}' no está registrado en el catálogo. Se incluye como referencia (N2) con 'esReferencia = true' y 'ejercicioId = null'."
            );
        }

        return (
            exercise,
            ExerciseValidationStatus.Uncataloged,
            $"El ejercicio '{exercise.ExerciseName}' no existe en el catálogo y la política activa no admite referencias."
        );
    }

    private async Task<RoutineSuggestionResponseDto> GenerateSafeDeterministicProposalAsync(
        RoutineSuggestionRequestDto originalRequest,
        CancellationToken cancellationToken)
    {
        // Generamos la rutina a partir de las reglas del motor determinístico
        // Para no depender directamente de Infrastructure en Application, catalogamos los ejercicios determinísticos
        var allCatalog = await catalogService.GetAllAsync(cancellationToken);

        // Tomamos una selección coherente de ejercicios reales del catálogo
        var catalogMap = allCatalog.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        var defaultExercises = new List<SuggestedExerciseDto>();

        void AddIfCatalog(string name, int sets, int reps, decimal pct)
        {
            if (catalogMap.TryGetValue(name, out var item))
            {
                defaultExercises.Add(new SuggestedExerciseDto(item.Name, sets, reps, pct, item.Id, IsReference: false));
            }
            else
            {
                // Intento por búsqueda
                var matched = allCatalog.FirstOrDefault(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    defaultExercises.Add(new SuggestedExerciseDto(matched.Name, sets, reps, pct, matched.Id, IsReference: false));
                }
            }
        }

        AddIfCatalog("Sentadilla con barra", 4, 6, 75.0m);
        AddIfCatalog("Press de banca plano con barra", 4, 6, 75.0m);
        AddIfCatalog("Remo con barra", 3, 8, 70.0m);
        AddIfCatalog("Plancha", 3, 30, 0.0m);

        if (defaultExercises.Count == 0 && allCatalog.Count > 0)
        {
            // Tomamos los primeros ejercicios del catálogo garantizando IDs reales
            foreach (var item in allCatalog.Take(4))
            {
                defaultExercises.Add(new SuggestedExerciseDto(item.Name, 3, 10, 70.0m, item.Id, IsReference: false));
            }
        }
        else if (defaultExercises.Count == 0)
        {
            // Fallback resiliente si el servicio de catálogo no responde
            defaultExercises.Add(new SuggestedExerciseDto("Sentadilla básica", 3, 10, 60.0m, null, IsReference: true));
            defaultExercises.Add(new SuggestedExerciseDto("Flexiones de brazos", 3, 10, 0.0m, null, IsReference: true));
        }

        return new RoutineSuggestionResponseDto(
            AthleteId: originalRequest.AthleteId,
            ProposedTitle: $"Plan Determinístico Seguro: Acondicionamiento Físico ({originalRequest.Goal})",
            JustificationSummary: "Propuesta generada mediante motor determinístico de respaldo seguro con ejercicios verificados del catálogo oficial.",
            Exercises: defaultExercises,
            ProviderUsed: "Reglas (Fallback Validador Semántico)"
        );
    }
}
