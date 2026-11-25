using Application.DTOs.Adaptation;
using Application.DTOs.Ai;

namespace Application.Common.Adaptation;

/// <summary>
/// Centraliza la aplicación de sustituciones biomecánicas autorizadas y la redacción
/// de motivos de adaptación, compartida entre <see cref="Application.Common.Ai.ExerciseSemanticValidator"/>
/// y <see cref="Application.UseCases.Ai.SuggestRoutine.SuggestRoutineUseCase"/> (KAN-466).
/// </summary>
public static class AuthorizedSubstituteHelper
{
    public static Dictionary<Guid, ExerciseRestrictionContextDto> BuildRestrictionsByExercise(
        AthleteAdaptationContextDto context) =>
        context.Restrictions
            .Where(r => r.Substitutes.Count > 0)
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First());

    public static Dictionary<Guid, ExerciseRestrictionContextDto> BuildAllRestrictionsByExercise(
        AthleteAdaptationContextDto context) =>
        context.Restrictions
            .Where(r => r.ExerciseId != Guid.Empty)
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.First());

    public static SuggestedExerciseDto CreateAdaptedExercise(
        SuggestedExerciseDto targetExercise,
        AuthorizedSubstituteDto substitute) =>
        targetExercise with
        {
            ExerciseId = substitute.ExerciseId,
            ExerciseName = substitute.Name,
            IsReference = false
        };

    public static AdaptationChangeDto CreateAdaptationChange(
        SuggestedExerciseDto originalExercise,
        ExerciseRestrictionContextDto restriction,
        AuthorizedSubstituteDto substitute,
        string reason) =>
        new(
            originalExercise.ExerciseId,
            originalExercise.ExerciseName,
            substitute.ExerciseId,
            substitute.Name,
            restriction.Zone ?? string.Empty,
            reason,
            restriction.Substitutes);

    public static (List<SuggestedExerciseDto> Exercises, List<AdaptationChangeDto> Changes, List<string> Warnings, bool AdaptedAny)
        AdaptRestrictedExercises(
            IReadOnlyList<SuggestedExerciseDto> sourceExercises,
            AthleteAdaptationContextDto context,
            IReadOnlyList<AdaptationChangeDto>? existingChanges = null,
            IReadOnlyList<string>? existingWarnings = null)
    {
        var restrictionsByExercise = BuildRestrictionsByExercise(context);
        var adaptedExercises = new List<SuggestedExerciseDto>(sourceExercises.Count);
        var changes = existingChanges?.ToList() ?? [];
        var warnings = existingWarnings?.ToList() ?? [];
        var adaptedAny = false;

        foreach (var exercise in sourceExercises)
        {
            if (exercise.ExerciseId is { } exId &&
                exId != Guid.Empty &&
                restrictionsByExercise.TryGetValue(exId, out var restriction))
            {
                var substitute = restriction.Substitutes[0];
                adaptedExercises.Add(CreateAdaptedExercise(exercise, substitute));

                var reason = BuildAdaptationReason(restriction, substitute.Name);
                if (!warnings.Contains(reason))
                    warnings.Add(reason);

                changes.Add(CreateAdaptationChange(exercise, restriction, substitute, reason));
                adaptedAny = true;
            }
            else
            {
                adaptedExercises.Add(exercise);
            }
        }

        return (adaptedExercises, changes, warnings, adaptedAny);
    }

    public static string BuildAdaptationReason(
        ExerciseRestrictionContextDto restriction,
        string substituteName)
    {
        var zone = string.IsNullOrWhiteSpace(restriction.Zone) ? "zona afectada" : restriction.Zone.Trim();
        var normalizedReason = NormalizeReason(zone, restriction.Reason);
        return $"Sustitución biomecánica por lesión de {zone} para {normalizedReason}. Sustituto autorizado: {substituteName}.";
    }

    public static string BuildUnauthorizedSubstituteReason(
        ExerciseRestrictionContextDto restriction,
        string proposedName,
        string substituteName) =>
        $"El sustituto propuesto '{proposedName}' no estaba autorizado. " +
        $"{BuildAdaptationReason(restriction, substituteName)}";

    public static string BuildRemovedRestrictedExerciseReason(
        ExerciseRestrictionContextDto restriction,
        string proposedName) =>
        BuildRemovedRestrictedExerciseReason(restriction.Zone, restriction.Reason, proposedName);

    public static string BuildRemovedRestrictedExerciseReason(
        string? zone,
        string? reason,
        string proposedName)
    {
        var displayZone = string.IsNullOrWhiteSpace(zone) ? "zona afectada" : zone.Trim();
        var normalizedReason = NormalizeReason(displayZone, reason);
        return $"Se quitó '{proposedName}' de la sesión por restricción de {displayZone} ({normalizedReason}) al no haber un sustituto autorizado libre.";
    }

    public static string NormalizeReason(string? zone, string? reason)
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
}
