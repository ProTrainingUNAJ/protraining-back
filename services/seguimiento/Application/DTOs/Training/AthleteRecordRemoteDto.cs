namespace Application.DTOs.Training;

public sealed record AthleteRecordRemoteDto(
    Guid Id,
    Guid AthleteId,
    Guid ExerciseId,
    string ExerciseName,
    decimal WeightOneRepMaxKg,
    string Origin,
    DateTime AchievedAt,
    bool IsCurrent);

