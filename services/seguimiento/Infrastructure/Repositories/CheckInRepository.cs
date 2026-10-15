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

    public Task AddAsync(CheckIn checkIn, CancellationToken cancellationToken = default)
    {
        context.CheckIns.Add(checkIn);
        return Task.CompletedTask;
    }
}