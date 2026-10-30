using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Repositorio EF Core para la memoria de decisiones del PF (pf_decisions, KAN-423 / KAN-410).
/// El aislamiento multi-tenant por PfId y DeletedAt lo aplica automáticamente AppDbContext.
/// </summary>
public sealed class PfDecisionRepository(AppDbContext dbContext) : IPfDecisionRepository
{
    public async Task AddAsync(PfDecision decision, CancellationToken cancellationToken = default)
    {
        await dbContext.PfDecisions.AddAsync(decision, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<PfDecision> decisions, CancellationToken cancellationToken = default)
    {
        await dbContext.PfDecisions.AddRangeAsync(decisions, cancellationToken);
    }

    public async Task<IReadOnlyList<PfDecision>> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        return await dbContext.PfDecisions
            .AsNoTracking()
            .Where(d => d.SourceId == sourceId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PfDecision>> GetRecentByAthleteAsync(
        Guid athleteId,
        string? zone = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.PfDecisions
            .AsNoTracking()
            .Where(d => d.AthleteId == athleteId);

        if (!string.IsNullOrWhiteSpace(zone))
        {
            var normalizedZone = zone.Trim();
            query = query.Where(d => d.Zone == normalizedZone);
        }

        return await query
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .Take(limit > 0 ? limit : 50)
            .ToListAsync(cancellationToken);
    }
}
