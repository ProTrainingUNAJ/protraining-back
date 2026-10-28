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

    public Task AddAsync(DailyLoad dailyLoad, CancellationToken cancellationToken = default)
    {
        context.DailyLoads.Add(dailyLoad);
        return Task.CompletedTask;
    }
}
