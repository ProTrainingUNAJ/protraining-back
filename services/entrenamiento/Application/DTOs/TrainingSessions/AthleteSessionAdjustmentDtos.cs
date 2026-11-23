using Domain.Entities;

namespace Application.DTOs.TrainingSessions;

public sealed record AthleteSessionAdjustmentItemDto(
    Guid OriginalExerciseId,
    Guid SubstituteExerciseId
);

public sealed record ApplyAthleteSessionAdjustmentRequest(
    Guid AthleteId,
    DateOnly Date,
    string? Note,
    IReadOnlyList<AthleteSessionAdjustmentItemDto>? Items = null,
    Guid? TrainingPlanId = null,
    int? LoadReductionPercentage = null
);

public sealed record RejectAthleteSessionAdjustmentRequest(
    Guid AthleteId,
    DateOnly? Date = null,
    string? Note = null,
    Guid? TrainingPlanId = null
);

public sealed record AthleteSessionAdjustmentResponseDto(
    Guid Id,
    Guid SessionId,
    Guid AthleteId,
    DateOnly Date,
    string? Note,
    IReadOnlyList<AthleteSessionAdjustmentItemDto> Items,
    Guid? TrainingPlanId = null,
    int? LoadReductionPercentage = null,
    string Status = AthleteSessionAdjustmentStatuses.Approved,
    string? RejectionReason = null
);
