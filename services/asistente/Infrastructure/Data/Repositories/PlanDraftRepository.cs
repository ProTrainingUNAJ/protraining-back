using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Repositorio EF Core para los borradores de planificación (plan_drafts, KAN-383 / KAN-410).
/// El aislamiento multi-tenant por PfId y DeletedAt lo aplica automáticamente AppDbContext.
/// </summary>
public sealed class PlanDraftRepository(AppDbContext dbContext) : IPlanDraftRepository
{
    public async Task AddAsync(PlanDraft planDraft, CancellationToken cancellationToken = default)
    {
        await dbContext.PlanDrafts.AddAsync(planDraft, cancellationToken);
    }

    public async Task<PlanDraft?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.PlanDrafts
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<PlanDraft?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.PlanDrafts
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }
}
