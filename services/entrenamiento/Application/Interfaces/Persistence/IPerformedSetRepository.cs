using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPerformedSetRepository
{
    Task AddAsync(PerformedSet performedSet, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<PerformedSet> performedSets, CancellationToken cancellationToken = default);
}

