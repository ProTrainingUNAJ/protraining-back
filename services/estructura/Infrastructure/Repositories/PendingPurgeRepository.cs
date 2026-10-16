using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PendingPurgeRepository(AppDbContext context) : IPendingPurgeRepository
{
    public Task AddAsync(PendingPurge pendingPurge, CancellationToken cancellationToken = default)
    {
        context.PendingPurges.Add(pendingPurge);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PendingPurge>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.PendingPurges
                .AsNoTracking()
                .Where(p => p.Status == PendingPurge.PendingStatus)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken));

    public async Task<PendingPurge?> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.PendingPurges
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId, cancellationToken));
}
