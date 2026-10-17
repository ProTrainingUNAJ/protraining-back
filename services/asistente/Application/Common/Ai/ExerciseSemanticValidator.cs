namespace Application.Common.Ai;

using Application.DTOs.Ai;
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

        return new RoutineSemanticValidationResult(
            IsValid: !requiresFallback,
            RequiresDeterministicFallback: requiresFallback,
            HasCorrections: hasCorrections,
            ExerciseResults: itemResults,
            SanitizedRoutine: sanitizedRoutine,
            ValidationMessages: messages
        );
    }

    public async Task<RoutineSuggestionResponseDto> ValidateOrFallbackAsync(
        RoutineSuggestionResponseDto draft,
        RoutineSuggestionRequestDto originalRequest,
        SemanticValidationPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateDraftAsync(draft, policy, cancellationToken);

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
