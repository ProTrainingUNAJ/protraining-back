using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.Repositories;

/// <summary>
/// Repositorio EF Core para recomendaciones generadas para revisión del PF (athlete_recommendations, KAN-491).
/// El aislamiento multi-tenant por PfId y DeletedAt lo aplica automáticamente AppDbContext.
/// </summary>
public sealed class AthleteRecommendationRepository(AppDbContext dbContext) : IAthleteRecommendationRepository
{
    public async Task AddAsync(AthleteRecommendation recommendation, CancellationToken cancellationToken = default)
    {
        await dbContext.AthleteRecommendations.AddAsync(recommendation, cancellationToken);
    }

    public async Task<AthleteRecommendation?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.AthleteRecommendations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<AthleteRecommendation?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.AthleteRecommendations
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<AthleteRecommendation>> ListAsync(
        Guid? athleteId = null,
        Guid? categoryId = null,
        RecommendationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AthleteRecommendations.AsNoTracking();

        if (athleteId.HasValue && athleteId.Value != Guid.Empty)
        {
            query = query.Where(r => r.AthleteId == athleteId.Value);
        }

        if (categoryId.HasValue && categoryId.Value != Guid.Empty)
        {
            query = query.Where(r => r.CategoryId == categoryId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AthleteRecommendation>> ListApprovedByAthleteAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            return [];

        return await dbContext.AthleteRecommendations
            .AsNoTracking()
            .Where(r => r.AthleteId == athleteId && r.Status == RecommendationStatus.Approved)
            .OrderByDescending(r => r.ReviewedAtUtc ?? r.UpdatedAt)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AthleteRecommendation>> ListByAthleteIdsAndStatusesAsync(
        IReadOnlyCollection<Guid> athleteIds,
        IReadOnlyCollection<RecommendationStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        if (athleteIds.Count == 0 || statuses.Count == 0)
            return [];

        var ids = athleteIds.Distinct().ToList();
        var statusList = statuses.Distinct().ToList();

        return await dbContext.AthleteRecommendations
            .AsNoTracking()
            .Where(r => ids.Contains(r.AthleteId) && statusList.Contains(r.Status))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
