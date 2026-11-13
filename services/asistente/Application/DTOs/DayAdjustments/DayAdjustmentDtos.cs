using Application.DTOs.DayConflicts;

namespace Application.DTOs.DayAdjustments;

public sealed record DayAdjustmentItemDto(
    Guid OriginalExerciseId,
    string OriginalExerciseName,
    Guid SuggestedSubstituteExerciseId,
    string SuggestedSubstituteExerciseName,
    Guid SubstituteExerciseId,
    string SubstituteExerciseName,
    string Zone,
    int PainLevel,
    string Reason,
    IReadOnlyList<DayConflictSubstituteDto> AvailableSubstitutes
);

public sealed record DayAdjustmentResponseDto(
    Guid Id,
    Guid AthleteId,
    DateOnly Date,
    Guid SessionId,
    string Status,
    IReadOnlyList<DayAdjustmentItemDto> Items,
    string Justification,
    string Provider,
    string? RejectionReason = null,
    DateTime? CreatedAt = null
);

public sealed record ApproveDayAdjustmentItemInputDto(
    Guid OriginalExerciseId,
    Guid SubstituteExerciseId
);

public sealed record ApproveDayAdjustmentRequestDto(
    IReadOnlyList<ApproveDayAdjustmentItemInputDto>? Items = null
);

public sealed record RejectDayAdjustmentRequestDto(
    string? Reason = null
);

public sealed record ApplyAthleteAdjustmentItemRemoteDto(
    Guid OriginalExerciseId,
    Guid SubstituteExerciseId
);

public sealed record ApplyAthleteAdjustmentRequestRemoteDto(
    Guid AthleteId,
    DateOnly Date,
    string? Note,
    IReadOnlyList<ApplyAthleteAdjustmentItemRemoteDto> Items
);
