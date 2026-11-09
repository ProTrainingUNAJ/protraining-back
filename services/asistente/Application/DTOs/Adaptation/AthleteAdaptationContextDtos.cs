namespace Application.DTOs.Adaptation;

public sealed record ActiveInjuryRemoteDto(
    Guid Id,
    Guid AthleteId,
    string Zone,
    string Description,
    DateTime InjuryDate,
    DateTime EstimatedReturnDate,
    string Status,
    DateTime? ClearedAt,
    DateTime CreatedAt);

public sealed record AthleteRecordRemoteDto(
    Guid Id,
    Guid AthleteId,
    Guid ExerciseId,
    string ExerciseName,
    decimal WeightOneRepMaxKg,
    string Origin,
    DateTime AchievedAt,
    bool IsCurrent);

public sealed record AuthorizedSubstituteDto(
    Guid ExerciseId,
    string Name);

public sealed record ExerciseRestrictionContextDto(
    Guid ExerciseId,
    string? Zone,
    string? Reason,
    IReadOnlyList<AuthorizedSubstituteDto> Substitutes);

public sealed record AthleteAdaptationContextDto(
    Guid AthleteId,
    IReadOnlyList<ActiveInjuryRemoteDto> ActiveInjuries,
    IReadOnlyList<AthleteRecordRemoteDto> CurrentRecords,
    IReadOnlyList<ExerciseRestrictionContextDto> Restrictions);
