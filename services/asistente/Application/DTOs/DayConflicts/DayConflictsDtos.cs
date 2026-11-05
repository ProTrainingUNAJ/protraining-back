using System.Text.Json.Serialization;

namespace Application.DTOs.DayConflicts;

public sealed record TodayCheckInDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("athleteId")] Guid AthleteId,
    [property: JsonPropertyName("date")] DateTime Date,
    [property: JsonPropertyName("painZones")] string? PainZones
);

public sealed record TodayRoutineExerciseRemoteDto(
    [property: JsonPropertyName("exerciseId")] Guid ExerciseId,
    [property: JsonPropertyName("name")] string Name
);

public sealed record TodayRoutineRemoteDto(
    [property: JsonPropertyName("athleteId")] Guid AthleteId,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("exercises")] IReadOnlyList<TodayRoutineExerciseRemoteDto> Exercises
);

public sealed record CheckExerciseRestrictionsRequestDto(
    [property: JsonPropertyName("painZones")] string? PainZones,
    [property: JsonPropertyName("exerciseIds")] IReadOnlyList<Guid> ExerciseIds
);

public sealed record DayConflictSubstituteDto(
    [property: JsonPropertyName("exerciseId")] Guid ExerciseId,
    [property: JsonPropertyName("name")] string Name
);

public sealed record ExerciseRestrictionCheckResultRemoteDto(
    [property: JsonPropertyName("exerciseId")] Guid ExerciseId,
    [property: JsonPropertyName("restricted")] bool Restricted,
    [property: JsonPropertyName("zone")] string? Zone,
    [property: JsonPropertyName("painLevel")] int? PainLevel,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("substitutes")] IReadOnlyList<DayConflictSubstituteDto> Substitutes
);

public sealed record DayConflictItemDto(
    [property: JsonPropertyName("exerciseId")] Guid ExerciseId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("zone")] string Zone,
    [property: JsonPropertyName("painLevel")] int PainLevel,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("substitutes")] IReadOnlyList<DayConflictSubstituteDto> Substitutes
);

public sealed record DayConflictsResponseDto(
    [property: JsonPropertyName("hasConflicts")] bool HasConflicts,
    [property: JsonPropertyName("painZones")] string? PainZones,
    [property: JsonPropertyName("conflicts")] IReadOnlyList<DayConflictItemDto> Conflicts,
    [property: JsonPropertyName("message")] string? Message = null
);
