using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPfDecisionRepository
{
    Task AddAsync(PfDecision decision, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<PfDecision> decisions, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PfDecision>> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PfDecision>> GetRecentByAthleteAsync(Guid athleteId, string? zone = null, int limit = 50, CancellationToken cancellationToken = default);
}
