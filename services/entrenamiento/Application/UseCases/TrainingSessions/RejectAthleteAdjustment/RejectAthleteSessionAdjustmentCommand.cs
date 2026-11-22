using Application.DTOs.TrainingSessions;

namespace Application.UseCases.TrainingSessions.RejectAthleteAdjustment;

public sealed record RejectAthleteSessionAdjustmentCommand(
    Guid SessionId,
    RejectAthleteSessionAdjustmentRequest Request
);
