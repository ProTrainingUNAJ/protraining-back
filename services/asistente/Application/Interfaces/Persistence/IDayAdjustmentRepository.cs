using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IDayAdjustmentRepository
{
    Task AddAsync(DayAdjustment adjustment, CancellationToken cancellationToken = default);
    Task<DayAdjustment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DayAdjustment?> GetPendingByAthleteAndDateAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default);
}
