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
        }
        );
}