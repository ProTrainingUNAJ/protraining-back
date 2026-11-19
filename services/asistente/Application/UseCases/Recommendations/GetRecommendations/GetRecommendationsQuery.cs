using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Recommendations.GetRecommendations;

public sealed record GetRecommendationsQuery(
    Guid? AthleteId = null,
    Guid? CategoryId = null,
    string? Status = null);

public sealed class GetRecommendationsQueryHandler(IAthleteRecommendationRepository repository)
{
    public async Task<IReadOnlyList<AthleteRecommendationResponseDto>> HandleAsync(
        GetRecommendationsQuery query,
        CancellationToken cancellationToken = default)
    {
        RecommendationStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<RecommendationStatus>(query.Status.Trim(), ignoreCase: true, out var status))
            {
                throw new DomainException("El estado solicitado no es válido. Valores permitidos: Pending, Approved, Discarded.");
            }

            parsedStatus = status;
        }

        var items = await repository.ListAsync(
            query.AthleteId,
            query.CategoryId,
            parsedStatus,
            cancellationToken);

        return items.Select(RecommendationMapper.ToResponseDto).ToList();
    }
}
