using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class DailyLoadRepository(AppDbContext context) : IDailyLoadRepository
{
    public async Task<DailyLoad?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.DailyLoads
                .FirstOrDefaultAsync(
                    load => load.AthleteId == athleteId && load.Date == date,
                    cancellationToken));

    public async Task<IReadOnlyList<DailyLoad>> GetByAthleteAndDateRangeAsync(Guid athleteId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.DailyLoads
                .AsNoTracking()
                .Where(load => load.AthleteId == athleteId
                    && load.Date >= from.ToDateTime(TimeOnly.MinValue)
                    && load.Date <= to.ToDateTime(TimeOnly.MaxValue))
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<DailyLoad>> GetByAthletesAndDateRangeAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            if (athleteIds.Count == 0)
                return [];

            var start = from.ToDateTime(TimeOnly.MinValue);
            var end = to.ToDateTime(TimeOnly.MaxValue);

            return await context.DailyLoads
                .AsNoTracking()
                .Where(load => athleteIds.Contains(load.AthleteId)
                    && load.Date >= start
                    && load.Date <= end)
                .ToListAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<DailyLoad>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.DailyLoads
                .AsNoTracking()
                .Where(load => athleteIds.Contains(load.AthleteId) && load.Date == date)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<DailyLoad>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.DailyLoads
                .AsNoTracking()
                .Where(load => load.Date == date)
                .ToListAsync(cancellationToken));

    public Task AddAsync(DailyLoad dailyLoad, CancellationToken cancellationToken = default)
    {
        context.DailyLoads.Add(dailyLoad);
        return Task.CompletedTask;
    }
}
