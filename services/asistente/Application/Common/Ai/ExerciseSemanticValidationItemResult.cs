namespace Application.Common.Ai;

using Application.DTOs.Ai;

/// <summary>
/// Resultado detallado de la validación semántica aplicada a un ejercicio individual.
/// </summary>
public sealed record ExerciseSemanticValidationItemResult(
    SuggestedExerciseDto OriginalExercise,
    SuggestedExerciseDto ValidatedExercise,
    ExerciseValidationStatus Status,
    string Message
);
