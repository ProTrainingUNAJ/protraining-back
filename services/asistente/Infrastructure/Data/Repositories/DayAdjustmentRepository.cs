using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Repositorio EF Core para las propuestas de ajuste del día (day_adjustments, KAN-452).
/// El aislamiento multi-tenant por PfId y DeletedAt lo aplica automáticamente AppDbContext.
/// </summary>
public sealed class DayAdjustmentRepository(AppDbContext dbContext) : IDayAdjustmentRepository
{
    public async Task AddAsync(DayAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        await dbContext.DayAdjustments.AddAsync(adjustment, cancellationToken);
    }

    public async Task<DayAdjustment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.DayAdjustments
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<DayAdjustment?> GetPendingByAthleteAndDateAsync(
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DayAdjustments
            .FirstOrDefaultAsync(
                d => d.AthleteId == athleteId && d.Date == date && d.Status == DayAdjustmentStatus.Pending,
                cancellationToken);
    }
}
