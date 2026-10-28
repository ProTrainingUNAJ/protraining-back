using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CompetitiveEventRepository(AppDbContext context) : ICompetitiveEventRepository
{
    public async Task<IReadOnlyList<CompetitiveEvent>> ListByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.CompetitiveEvents
                .AsNoTracking()
                .Where(e => e.CategoryId == categoryId)
                .OrderBy(e => e.Date)
                .ThenBy(e => e.StartTime)
                .ToListAsync(cancellationToken));

    public async Task<CompetitiveEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.CompetitiveEvents
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken));

    public Task AddAsync(CompetitiveEvent competitiveEvent, CancellationToken cancellationToken = default)
    {
        context.CompetitiveEvents.Add(competitiveEvent);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(CompetitiveEvent competitiveEvent, CancellationToken cancellationToken = default)
    {
        context.CompetitiveEvents.Remove(competitiveEvent);
        return Task.CompletedTask;
    }
}
