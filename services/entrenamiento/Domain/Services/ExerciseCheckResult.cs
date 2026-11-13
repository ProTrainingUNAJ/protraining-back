namespace Domain.Services;

public record ExerciseCheckResult(
    Guid ExerciseId,
    bool Restricted,
    string? Zone,
    int? PainLevel,
    string? Reason,
    IReadOnlyList<ExerciseSubstituteResult> Substitutes);

