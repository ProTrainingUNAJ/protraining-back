using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class AthleteSessionAdjustmentRepository(AppDbContext context) : IAthleteSessionAdjustmentRepository
{
    public async Task<AthleteSessionAdjustment?> GetBySessionAthleteAndDateAsync(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await context.AthleteSessionAdjustments
            .FirstOrDefaultAsync(
                a => a.TrainingSessionId == trainingSessionId && a.AthleteId == athleteId && a.Date == date,
                cancellationToken);
    }

    public async Task<AthleteSessionAdjustment?> GetBySessionAthleteAndDateAsNoTrackingAsync(
        Guid trainingSessionId,
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await context.AthleteSessionAdjustments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.TrainingSessionId == trainingSessionId && a.AthleteId == athleteId && a.Date == date,
                cancellationToken);
    }

    public async Task AddAsync(AthleteSessionAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        await context.AthleteSessionAdjustments.AddAsync(adjustment, cancellationToken);
    }
}
