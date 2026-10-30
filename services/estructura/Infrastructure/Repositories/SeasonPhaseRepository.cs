using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class SeasonPhaseRepository(AppDbContext context) : ISeasonPhaseRepository
{
    public async Task<IReadOnlyList<SeasonPhase>> ListByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.SeasonPhases
                .AsNoTracking()
                .Where(p => p.CategoryId == categoryId)
                .OrderBy(p => p.StartDate)
                .ThenBy(p => p.Id)
                .ToListAsync(cancellationToken));

    public async Task<SeasonPhase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.SeasonPhases
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken));

    public Task AddAsync(SeasonPhase phase, CancellationToken cancellationToken = default)
    {
        context.SeasonPhases.Add(phase);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(SeasonPhase phase, CancellationToken cancellationToken = default)
    {
        context.SeasonPhases.Remove(phase);
        return Task.CompletedTask;
    }

    public async Task<bool> HasOverlappingPhaseAsync(
        Guid categoryId,
        DateOnly startDate,
        DateOnly endDate,
        Guid? excludePhaseId = null,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.SeasonPhases
                .Where(p => p.CategoryId == categoryId
                    && (excludePhaseId == null || p.Id != excludePhaseId.Value)
                    && startDate <= p.EndDate
                    && endDate >= p.StartDate)
                .AnyAsync(cancellationToken));
}
