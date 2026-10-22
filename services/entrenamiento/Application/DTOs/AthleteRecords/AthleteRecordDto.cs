namespace Application.DTOs.AthleteRecords;

public record AthleteRecordDto(
    Guid Id,
    Guid AthleteId,
    Guid ExerciseId,
    string ExerciseName,
    decimal WeightOneRepMaxKg,
    string Origin,
    DateTime AchievedAt,
    bool IsCurrent
);

public record SaveAthleteRecordRequest(
    Guid ExerciseId,
    decimal WeightOneRepMaxKg,
    string Origin
);
