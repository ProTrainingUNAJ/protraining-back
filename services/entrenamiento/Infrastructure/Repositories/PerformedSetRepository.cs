using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

    public async Task<List<PerformedSet>> GetByAthleteAndDateAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return await _context.PerformedSets.Where(p => p.AthleteId == athleteId && p.Date == date).ToListAsync();
    }
}

