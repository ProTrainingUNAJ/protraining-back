namespace Application.Common.Ai;

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
            var adaptation = ApplyAuthorizedAdaptations(sanitizedRoutine, adaptationContext, initialItemResults, messages, itemResults);
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

    private static AdaptationValidationResult ApplyAuthorizedAdaptations(
        RoutineSuggestionResponseDto routine,
        AthleteAdaptationContextDto context,
        IReadOnlyList<ExerciseSemanticValidationItemResult> initialItemResults,
        ICollection<string> messages,
        ICollection<ExerciseSemanticValidationItemResult> itemResults)
    {
        var hasExplicitBaseExercises = routine.BaseExercises is not null;
        var baseExercises = routine.BaseExercises ?? routine.Exercises.ToList();

        if (context.Restrictions.Count == 0)
            return new(routine with { BaseExercises = baseExercises }, false);

        var restrictionsByExercise = context.Restrictions
            .Where(r => r.Substitutes.Count > 0)
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First());

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
                var message = BuildAdaptationReason(restriction, substitute.Name);
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
                var message = BuildUnauthorizedSubstituteReason(baseRestriction, exercise.ExerciseName, substitute.Name);
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

        // Pasada 2: Alucinaciones / ejercicios fuera de catálogo en sesiones de adaptación de 1 o múltiples ejercicios
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

            var applicableRestriction = restrictionsByExercise.Values
                .FirstOrDefault(r => !satisfiedRestrictionIds.Contains(r.ExerciseId))
                ?? (current.IsReference ? restrictionsByExercise.Values.First() : null);

            if (applicableRestriction is null)
                continue;

            var presentIds = CollectValidCatalogExerciseIds(exercises);
            var substitute = applicableRestriction.Substitutes
                .FirstOrDefault(s => !presentIds.Contains(s.ExerciseId))
                ?? applicableRestriction.Substitutes[0];

            var originalHallucinated = i < initialItemResults.Count ? initialItemResults[i].OriginalExercise : current;
            var message = BuildUnauthorizedSubstituteReason(applicableRestriction, originalHallucinated.ExerciseName, substitute.Name);

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
                var message = BuildUnauthorizedSubstituteReason(restriction, proposed.ExerciseName, substitute.Name);
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
        var adapted = targetExercise with
        {
            ExerciseId = substitute.ExerciseId,
            ExerciseName = substitute.Name,
            IsReference = false
        };

        messages.Add(message);
        changes.Add(new(
            originalForChange.ExerciseId,
            originalForChange.ExerciseName,
            substitute.ExerciseId,
            substitute.Name,
            restriction.Zone ?? string.Empty,
            message,
            restriction.Substitutes));
        itemResults.Add(new ExerciseSemanticValidationItemResult(
            targetExercise,
            adapted,
            ExerciseValidationStatus.AdaptedToAuthorizedSubstitute,
            message));

        return adapted;
    }

    private static string BuildAdaptationReason(
        ExerciseRestrictionContextDto restriction,
        string substituteName) =>
        $"Sustitución biomecánica por lesión de {restriction.Zone ?? "zona afectada"} para {NormalizeReason(restriction.Zone, restriction.Reason)}. Sustituto autorizado: {substituteName}.";

    private static string BuildUnauthorizedSubstituteReason(
        ExerciseRestrictionContextDto restriction,
        string proposedName,
        string substituteName) =>
        $"El sustituto propuesto '{proposedName}' no estaba autorizado. " +
        $"{BuildAdaptationReason(restriction, substituteName)}";

    private static string NormalizeReason(string? zone, string? reason)
    {
        if (string.Equals(zone?.Trim(), "rodilla", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(reason) &&
            reason.Contains("rotul", StringComparison.OrdinalIgnoreCase))
        {
            return "reducir estrés en tendón rotuliano";
        }

        var normalized = string.IsNullOrWhiteSpace(reason)
            ? "reducir el estrés de la zona lesionada"
            : reason.Trim().TrimEnd('.');
        return char.ToLowerInvariant(normalized[0]) + normalized[1..];
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
