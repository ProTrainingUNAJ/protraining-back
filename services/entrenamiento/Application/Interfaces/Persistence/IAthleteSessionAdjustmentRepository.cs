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

    Task AddAsync(AthleteSessionAdjustment adjustment, CancellationToken cancellationToken = default);
}
