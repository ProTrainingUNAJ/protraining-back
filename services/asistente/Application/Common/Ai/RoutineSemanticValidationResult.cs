namespace Application.Common.Ai;

using Application.DTOs.Ai;

/// <summary>
/// Resultado integral de la inspección semántica de una propuesta de rutina.
/// </summary>
public sealed record RoutineSemanticValidationResult(
    bool IsValid,
    bool RequiresDeterministicFallback,
    bool HasCorrections,
    IReadOnlyList<ExerciseSemanticValidationItemResult> ExerciseResults,
    RoutineSuggestionResponseDto SanitizedRoutine,
    IReadOnlyList<string> ValidationMessages
)
{
    public int CorrectedCount => ExerciseResults.Count(e => e.Status == ExerciseValidationStatus.IdCorrected);
    public int ReferenceCount => ExerciseResults.Count(e => e.Status == ExerciseValidationStatus.BroughtAsReference);
    public int ValidCount => ExerciseResults.Count(e => e.Status == ExerciseValidationStatus.Valid);
    public int UncatalogedCount => ExerciseResults.Count(e => e.Status == ExerciseValidationStatus.Uncataloged);
    public int HallucinatedCount => UncatalogedCount;
}
