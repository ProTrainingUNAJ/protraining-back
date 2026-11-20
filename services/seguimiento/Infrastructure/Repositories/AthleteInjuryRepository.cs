using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Domain.Common;
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

    public async Task<IReadOnlyList<AthleteInjury>> ListNotClearedByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteInjuries
                .AsNoTracking()
                .Where(injury => injury.AthleteId == athleteId && injury.Status != InjuryStatus.MedicallyCleared)
                .OrderByDescending(injury => injury.InjuryDate)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveByAthleteIdsAsync(
        IReadOnlyCollection<Guid> athleteIds,
        CancellationToken cancellationToken = default)
    {
        if (athleteIds.Count == 0)
            return new Dictionary<Guid, int>();

        var ids = athleteIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, int>();

        return await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteInjuries
                .AsNoTracking()
                .Where(injury => ids.Contains(injury.AthleteId) && injury.Status == InjuryStatus.Active)
                .GroupBy(injury => injury.AthleteId)
                .Select(group => new { AthleteId = group.Key, ActiveCount = group.Count() })
                .ToDictionaryAsync(item => item.AthleteId, item => item.ActiveCount, cancellationToken));
    }

    public async Task<InjuryPeriodAggregateDto> GetPeriodAggregateByAthleteIdsAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var ids = athleteIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new InjuryPeriodAggregateDto(0, 0, 0, 0);

        var fromDate = from.ToDateTime(TimeOnly.MinValue);
        var toDate = to.ToDateTime(TimeOnly.MaxValue);
        // ClearedAt es timestamptz: Npgsql exige DateTime con Kind=Utc. El rango se
        // interpreta en hora Argentina para que el día del alta coincida con el contrato.
        var fromUtc = ArgentinaTime.ToUtcStartOfDay(from);
        var toUtc = ArgentinaTime.ToUtcEndOfDay(to);

        // Se incluyen las lesiones vigentes dentro del rango y las dadas de alta en él.
        // El filtro global por PfId del AppDbContext ya limita las filas al PF del token.
        var injuries = await PersistenceErrors.ExecuteAsync(async () =>
            await context.AthleteInjuries
                .AsNoTracking()
                .Where(injury => ids.Contains(injury.AthleteId)
                    && injury.InjuryDate <= toDate
                    && (injury.Status != InjuryStatus.MedicallyCleared
                        || (injury.ClearedAt >= fromUtc && injury.ClearedAt <= toUtc)))
                .Select(injury => new { injury.AthleteId, injury.Status })
                .ToListAsync(cancellationToken));

        return new InjuryPeriodAggregateDto(
            injuries.Count(injury => injury.Status == InjuryStatus.Active),
            injuries.Count(injury => injury.Status == InjuryStatus.InRecovery),
            injuries.Count(injury => injury.Status == InjuryStatus.MedicallyCleared),
            injuries.Select(injury => injury.AthleteId).Distinct().Count());
    }

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
