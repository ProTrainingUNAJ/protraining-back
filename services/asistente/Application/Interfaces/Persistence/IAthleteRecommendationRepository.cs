using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteRecommendationRepository
{
    Task AddAsync(AthleteRecommendation recommendation, CancellationToken cancellationToken = default);

    Task<AthleteRecommendation?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AthleteRecommendation?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AthleteRecommendation>> ListAsync(
        Guid? athleteId = null,
        Guid? categoryId = null,
        RecommendationStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AthleteRecommendation>> ListApprovedByAthleteAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AthleteRecommendation>> ListPendingByAthleteIdsAsync(
        IReadOnlyCollection<Guid> athleteIds,
        CancellationToken cancellationToken = default);
}
