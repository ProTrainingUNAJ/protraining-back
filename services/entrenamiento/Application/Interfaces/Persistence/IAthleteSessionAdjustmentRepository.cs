using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteSessionAdjustmentRepository
{
    Task<AthleteSessionAdjustment?> GetBySessionAthleteAndDateAsync(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    Task<AthleteSessionAdjustment?> GetBySessionAthleteAndDateAsNoTrackingAsync(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AthleteSessionAdjustment>> ListByAthleteAndPlanAsNoTrackingAsync(
        Guid athleteId,
        Guid trainingPlanId,
        CancellationToken cancellationToken = default);

    Task AddAsync(AthleteSessionAdjustment adjustment, CancellationToken cancellationToken = default);
}
