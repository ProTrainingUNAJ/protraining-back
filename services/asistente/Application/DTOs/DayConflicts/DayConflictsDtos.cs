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
    Guid? SessionId = null,
    // Entrenamiento lo marca cuando el PF ya ajustó la sesión de ese día para el atleta
    bool AdjustedByCoach = false
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

public sealed record TrafficLightIndicatorsRemoteDto(
    string? PainZones
);

public sealed record CategoryTrafficLightAthleteRemoteDto(
    Guid AthleteId,
    bool HasReportedToday,
    TrafficLightIndicatorsRemoteDto? Indicators
);

public sealed record CategoryAthleteDayConflictDto(
    Guid AthleteId,
    bool HasConflicts,
    // El PF ya ajustó la sesión de hoy: no se vuelve a ofrecer el ajuste (KAN-660, hallazgo 3)
    bool AdjustedToday = false
);

public sealed record CategoryDayConflictsResponseDto(
    Guid CategoryId,
    IReadOnlyList<CategoryAthleteDayConflictDto> Athletes
);
