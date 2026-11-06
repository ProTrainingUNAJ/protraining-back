namespace Application.DTOs.DayConflicts;

public sealed record TodayCheckInDto(
    Guid Id,
    Guid AthleteId,
    DateTime Date,
    string? PainZones
);

public sealed record TodayRoutineExerciseRemoteDto(
    Guid ExerciseId,
    string Name
);

public sealed record TodayRoutineRemoteDto(
    Guid AthleteId,
    DateOnly Date,
    IReadOnlyList<TodayRoutineExerciseRemoteDto> Exercises,
    Guid? SessionId = null
);

public sealed record CheckExerciseRestrictionsRequestDto(
    string? PainZones,
    IReadOnlyList<Guid> ExerciseIds
);

public sealed record DayConflictSubstituteDto(
    Guid ExerciseId,
    string Name
);

public sealed record ExerciseRestrictionCheckResultRemoteDto(
    Guid ExerciseId,
    bool Restricted,
    string? Zone,
    int? PainLevel,
    string? Reason,
    IReadOnlyList<DayConflictSubstituteDto> Substitutes
);

public sealed record DayConflictItemDto(
    Guid ExerciseId,
    string Name,
    string Zone,
    int PainLevel,
    string Reason,
    IReadOnlyList<DayConflictSubstituteDto> Substitutes
);

public sealed record DayConflictsResponseDto(
    bool HasConflicts,
    string? PainZones,
    IReadOnlyList<DayConflictItemDto> Conflicts,
    string? Message = null,
    Guid? SessionId = null
);
