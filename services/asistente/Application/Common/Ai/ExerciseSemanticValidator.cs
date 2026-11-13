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
            var adaptation = ApplyAuthorizedAdaptations(sanitizedRoutine, adaptationContext, messages, itemResults);
            sanitizedRoutine = adaptation.Routine;
            hasCorrections |= adaptation.HasCorrections;
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
        ICollection<string> messages,
        ICollection<ExerciseSemanticValidationItemResult> itemResults)
    {
        if (context.Restrictions.Count == 0)
            return new(routine, false);

        var restrictionsByExercise = context.Restrictions
            .Where(r => r.Substitutes.Count > 0)
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First());

        var exercises = new List<SuggestedExerciseDto>(routine.Exercises.Count);
        var changes = new List<AdaptationChangeDto>();
        var adaptedAny = false;
        foreach (var exercise in routine.Exercises)
        {
            if (exercise.ExerciseId is not { } exerciseId ||
                !restrictionsByExercise.TryGetValue(exerciseId, out var restriction))
            {
                exercises.Add(exercise);
                continue;
            }

            var substitute = restriction.Substitutes[0];
            var adapted = exercise with
            {
                ExerciseId = substitute.ExerciseId,
                ExerciseName = substitute.Name,
                IsReference = false
            };
            exercises.Add(adapted);
            adaptedAny = true;
            var message =
                BuildAdaptationReason(restriction, substitute.Name);
            messages.Add(message);
            changes.Add(new(
                exercise.ExerciseId,
                exercise.ExerciseName,
                substitute.ExerciseId,
                substitute.Name,
                restriction.Zone ?? string.Empty,
                message));
            itemResults.Add(new ExerciseSemanticValidationItemResult(
                exercise,
                adapted,
                ExerciseValidationStatus.AdaptedToAuthorizedSubstitute,
                message));
        }

        if (!adaptedAny && restrictionsByExercise.Count == 1 && routine.Exercises.Count == 1)
        {
            var restriction = restrictionsByExercise.Values.Single();
            var proposed = routine.Exercises[0];
            var authorizedIds = restriction.Substitutes.Select(substitute => substitute.ExerciseId).ToHashSet();
            if (proposed.ExerciseId is not { } proposedId || !authorizedIds.Contains(proposedId))
            {
                var substitute = restriction.Substitutes[0];
                var adapted = proposed with
                {
                    ExerciseId = substitute.ExerciseId,
                    ExerciseName = substitute.Name,
                    IsReference = false
                };
                var message =
                    BuildUnauthorizedSubstituteReason(restriction, proposed.ExerciseName, substitute.Name);
                messages.Add(message);
                changes.Add(new(
                    proposed.ExerciseId,
                    proposed.ExerciseName,
                    substitute.ExerciseId,
                    substitute.Name,
                    restriction.Zone ?? string.Empty,
                    message));
                itemResults.Add(new ExerciseSemanticValidationItemResult(
                    proposed,
                    adapted,
                    ExerciseValidationStatus.AdaptedToAuthorizedSubstitute,
                    message));
                exercises[0] = adapted;
                adaptedAny = true;
            }
        }

        return new(
            routine with
        {
            Exercises = exercises,
            WasAdapted = adaptedAny,
            AdaptationWarnings = messages.Where(message => message.Contains("sustituto autorizado", StringComparison.OrdinalIgnoreCase)).ToList(),
            AdaptationChanges = changes
        },
            adaptedAny);
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
