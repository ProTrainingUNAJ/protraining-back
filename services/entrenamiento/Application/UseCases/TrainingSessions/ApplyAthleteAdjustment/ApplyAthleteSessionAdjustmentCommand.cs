using Application.DTOs.TrainingSessions;

namespace Application.UseCases.TrainingSessions.ApplyAthleteAdjustment;

public sealed record ApplyAthleteSessionAdjustmentCommand(
    Guid SessionId,
    ApplyAthleteSessionAdjustmentRequest Request
);
