using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Recommendations.GetMyRecommendations;

public sealed record GetMyRecommendationsQuery;

/// <summary>
/// Devuelve las recomendaciones aprobadas por el PF para el atleta autenticado (#197 · KAN-492).
/// El identificador de atleta siempre sale del token a través de Estructura GET /api/v1/me.
/// </summary>
public sealed class GetMyRecommendationsQueryHandler(
    IStructureAthleteClient structureClient,
    IAthleteRecommendationRepository repository)
{
    public async Task<IReadOnlyList<MyRecommendationResponseDto>> HandleAsync(
        GetMyRecommendationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var athleteId = await structureClient.GetMyAthleteIdAsync(cancellationToken);
        if (!athleteId.HasValue || athleteId.Value == Guid.Empty)
            return [];

        var approved = await repository.ListApprovedByAthleteAsync(athleteId.Value, cancellationToken);
        return approved.Select(RecommendationMapper.ToMyResponseDto).ToList();
    }
}
