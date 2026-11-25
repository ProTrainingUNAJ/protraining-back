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
                // IgnoreQueryFilters: El worker en segundo plano (DataPurgeWorker) procesa las purgas pendientes de todos los preparadores físicos a nivel de sistema, sin un contexto de usuario PF específico.
                .IgnoreQueryFilters()
                .Where(p => p.Status == PendingPurge.PendingStatus && p.DeletedAt == null)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken));

    public async Task<PendingPurge?> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.PendingPurges
                // IgnoreQueryFilters: Búsqueda administrativa de estado de purga independiente del preparador físico autenticado.
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.AthleteId == athleteId && p.DeletedAt == null, cancellationToken));
}
