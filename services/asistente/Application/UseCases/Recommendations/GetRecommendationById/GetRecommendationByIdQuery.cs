using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Recommendations.GetRecommendationById;

public sealed record GetRecommendationByIdQuery(Guid Id);

public sealed class GetRecommendationByIdQueryHandler(IAthleteRecommendationRepository repository)
{
    public async Task<AthleteRecommendationResponseDto> HandleAsync(
        GetRecommendationByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var recommendation = await repository.GetByIdAsNoTrackingAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException("La recomendación solicitada no fue encontrada.");

        return RecommendationMapper.ToResponseDto(recommendation);
    }
}
