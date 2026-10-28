using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPfDecisionRepository
{
    Task AddRangeAsync(IEnumerable<PfDecision> decisions, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PfDecision>> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default);
}
