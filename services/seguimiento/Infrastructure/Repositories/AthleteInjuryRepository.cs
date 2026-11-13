using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AthleteInjuryRepository(AppDbContext context) : IAthleteInjuryRepository
{
    public async Task<AthleteInjury?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteInjuries
                .FirstOrDefaultAsync(injury => injury.Id == id, cancellationToken));

    // El filtro global por PfId del AppDbContext ya limita las lesiones al PF del token.
    public async Task<IReadOnlyList<AthleteInjury>> ListByAthleteAsync(Guid athleteId, InjuryStatus? status, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteInjuries
                .AsNoTracking()
                .Where(injury => injury.AthleteId == athleteId
                    && (status == null || injury.Status == status))
                .OrderByDescending(injury => injury.InjuryDate)
                .ToListAsync(cancellationToken));

    public Task AddAsync(AthleteInjury injury, CancellationToken cancellationToken = default)
    {
        context.AthleteInjuries.Add(injury);
        return Task.CompletedTask;
    }

    public Task AddStatusChangeAsync(AthleteInjuryStatusChange statusChange, CancellationToken cancellationToken = default)
    {
        context.AthleteInjuryStatusChanges.Add(statusChange);
        return Task.CompletedTask;
    }
}
