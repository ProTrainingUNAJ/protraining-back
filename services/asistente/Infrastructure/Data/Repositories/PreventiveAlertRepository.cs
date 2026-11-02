using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Repositorio EF Core para avisos preventivos con propuesta de ajuste (preventive_alerts, KAN-474).
/// El aislamiento multi-tenant por PfId y DeletedAt lo aplica automáticamente AppDbContext.
/// </summary>
public sealed class PreventiveAlertRepository(AppDbContext dbContext) : IPreventiveAlertRepository
{
    public async Task AddAsync(PreventiveAlert alert, CancellationToken cancellationToken = default)
    {
        await dbContext.PreventiveAlerts.AddAsync(alert, cancellationToken);
    }

    public async Task<PreventiveAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.PreventiveAlerts
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<PreventiveAlert?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.PreventiveAlerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PreventiveAlert>> ListAsync(
        bool onlyUnreviewed = true,
        AlertSeverity? severity = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.PreventiveAlerts.AsNoTracking();

        if (onlyUnreviewed)
        {
            query = query.Where(a => a.Status == AlertStatus.Pending);
        }

        if (severity.HasValue)
        {
            query = query.Where(a => a.Severity == severity.Value);
        }

        return await query
            .OrderByDescending(a => a.GeneratedAtUtc)
            .ToListAsync(cancellationToken);
    }
}

