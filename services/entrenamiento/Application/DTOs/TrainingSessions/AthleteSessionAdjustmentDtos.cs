namespace Application.DTOs.TrainingSessions;

public sealed record AthleteSessionAdjustmentItemDto(
    Guid OriginalExerciseId,
    Guid SubstituteExerciseId
);

public sealed record ApplyAthleteSessionAdjustmentRequest(
    Guid AthleteId,
    DateOnly Date,
    string? Note,
    IReadOnlyList<AthleteSessionAdjustmentItemDto> Items,
    Guid? TrainingPlanId = null
);

public sealed record AthleteSessionAdjustmentResponseDto(
    Guid Id,
    Guid SessionId,
    Guid AthleteId,
    DateOnly Date,
    string? Note,
    IReadOnlyList<AthleteSessionAdjustmentItemDto> Items,
    Guid? TrainingPlanId = null
);
