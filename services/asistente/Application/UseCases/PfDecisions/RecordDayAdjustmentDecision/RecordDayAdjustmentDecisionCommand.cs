using Application.DTOs.PfDecisions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.PfDecisions.RecordDayAdjustmentDecision;

public sealed record RecordDayAdjustmentDecisionCommand(
    Guid AthleteId,
    Guid SourceId,
    Guid SuggestedExerciseId,
    PfDecisionType Decision,
    Guid? FinalExerciseId = null,
    decimal? FinalPercentage = null,
    string? Zone = null,
    string? Reason = null,
    DateOnly? Date = null
);

public sealed class RecordDayAdjustmentDecisionCommandHandler(
    IPfDecisionRepository decisionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<PfDecisionResponseDto> HandleAsync(
        RecordDayAdjustmentDecisionCommand command,
        CancellationToken cancellationToken = default)
    {
        var decision = PfDecision.Create(
            athleteId: command.AthleteId,
            source: PfDecisionSource.DayAdjustment,
            sourceId: command.SourceId,
            suggestedExerciseId: command.SuggestedExerciseId,
            decision: command.Decision,
            finalExerciseId: command.FinalExerciseId,
            finalPercentage: command.FinalPercentage,
            zone: command.Zone,
            reason: command.Reason,
            date: command.Date);

        await decisionRepository.AddAsync(decision, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PfDecisionResponseDto(
            Id: decision.Id,
            AthleteId: decision.AthleteId,
            Date: decision.Date,
            Source: decision.Source.ToString(),
            SourceId: decision.SourceId,
            Zone: decision.Zone,
            SuggestedExerciseId: decision.SuggestedExerciseId,
            Decision: decision.Decision.ToString(),
            FinalExerciseId: decision.FinalExerciseId,
            FinalPercentage: decision.FinalPercentage,
            Reason: decision.Reason,
            CreatedAt: decision.CreatedAt);
    }
}

