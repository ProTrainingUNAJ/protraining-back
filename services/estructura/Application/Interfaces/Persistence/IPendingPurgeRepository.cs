using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPendingPurgeRepository
{
    Task AddAsync(PendingPurge pendingPurge, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingPurge>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task<PendingPurge?> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default);
}
