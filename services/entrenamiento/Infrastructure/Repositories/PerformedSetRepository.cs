using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public class PerformedSetRepository : IPerformedSetRepository
{
    private readonly AppDbContext _context;

    public PerformedSetRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(PerformedSet performedSet, CancellationToken cancellationToken = default)
    {
        await _context.PerformedSets.AddAsync(performedSet, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<PerformedSet> performedSets, CancellationToken cancellationToken = default)
    {
        await _context.PerformedSets.AddRangeAsync(performedSets, cancellationToken);
    }
}

