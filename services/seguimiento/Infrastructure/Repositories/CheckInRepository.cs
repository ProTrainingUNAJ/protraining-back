using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class CheckInRepository(AppDbContext context) : ICheckInRepository
{
    public async Task<CheckIn?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.CheckIns
                .FirstOrDefaultAsync(
                    checkIn => checkIn.AthleteId == athleteId && checkIn.Date == date,
                    cancellationToken));

    public async Task<IReadOnlyList<CheckIn>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.CheckIns
                .AsNoTracking()
                .Where(checkIn => athleteIds.Contains(checkIn.AthleteId) && checkIn.Date == date)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<CheckIn>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.CheckIns
                .AsNoTracking()
                .Where(checkIn => checkIn.Date == date)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<CheckIn>> GetByAthletesAndDateRangeAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            if (athleteIds.Count == 0)
                return [];

            return await context.CheckIns
                .AsNoTracking()
                .Where(checkIn => athleteIds.Contains(checkIn.AthleteId) && checkIn.Date >= startDate && checkIn.Date <= endDate)
                .ToListAsync(cancellationToken);
        });

    public Task AddAsync(CheckIn checkIn, CancellationToken cancellationToken = default)
    {
        context.CheckIns.Add(checkIn);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Guid>> GetCompletedCheckInAthleteIdsAsync(
        DateOnly date,
        Guid pfId,
        IReadOnlyList<Guid> athleteIds,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            if (athleteIds.Count == 0)
                return [];

            var targetDate = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var localDate = date.ToDateTime(TimeOnly.MinValue);

            return await context.CheckIns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.PfId == pfId && (c.Date == targetDate || c.Date == localDate) && athleteIds.Contains(c.AthleteId))
                .Select(c => c.AthleteId)
                .Distinct()
                .ToListAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<CheckIn>> GetHistoryByAthletesAndDateRangeAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            if (athleteIds.Count == 0)
                return [];

            return await context.CheckIns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => athleteIds.Contains(c.AthleteId) && c.Date >= startDate && c.Date <= endDate)
                .OrderBy(c => c.Date)
                .ToListAsync(cancellationToken);
        });

    public async Task<IReadOnlyList<CheckIn>> GetByAthleteAndDateRangeAsync(
        Guid athleteId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            var query = context.CheckIns
                .AsNoTracking()
                .Where(c => c.AthleteId == athleteId);

            if (startDate.HasValue)
                query = query.Where(c => c.Date >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(c => c.Date <= endDate.Value);

            // Si hay un límite y no se especificó un rango fijo de fechas, tomamos los N más recientes y los ordenamos cronológicamente.
            if (limit.HasValue && limit.Value > 0 && !startDate.HasValue && !endDate.HasValue)
            {
                var recent = await query
                    .OrderByDescending(c => c.Date)
                    .Take(limit.Value)
                    .ToListAsync(cancellationToken);

                return recent.OrderBy(c => c.Date).ToList();
            }

            query = query.OrderBy(c => c.Date);

            if (limit.HasValue && limit.Value > 0)
                query = query.Take(limit.Value);

            return await query.ToListAsync(cancellationToken);
        });
}
