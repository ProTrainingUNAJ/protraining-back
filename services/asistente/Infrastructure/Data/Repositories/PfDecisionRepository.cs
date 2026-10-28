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
}
