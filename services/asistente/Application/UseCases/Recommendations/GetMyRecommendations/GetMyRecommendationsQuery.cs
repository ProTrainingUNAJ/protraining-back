using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Recommendations.GetMyRecommendations;

public sealed record GetMyRecommendationsQuery;

/// <summary>
/// Devuelve las recomendaciones aprobadas por el PF para el atleta autenticado (#197 · KAN-492).
/// El identificador de atleta siempre sale del token a través de Estructura GET /api/v1/me.
/// KAN-493: antes de devolverlas exige que el PF tenga habilitadas las recomendaciones y que
/// el atleta menor cuente con autorización de su tutor. La restricción se aplica en backend,
/// también para acceso directo al endpoint, y ante fallos de comunicación se bloquea (fail-closed).
/// </summary>
public sealed class GetMyRecommendationsQueryHandler(
    IStructureAthleteClient structureClient,
    IIdentityHabitsRecommendationsClient identityClient,
    IAthleteRecommendationRepository repository)
{
    public async Task<IReadOnlyList<MyRecommendationResponseDto>> HandleAsync(
        GetMyRecommendationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var athleteId = await structureClient.GetMyAthleteIdAsync(cancellationToken);
        if (!athleteId.HasValue || athleteId.Value == Guid.Empty)
            return [];

        var pfEnabled = await identityClient.GetAthletePfHabitsRecommendationsEnabledAsync(cancellationToken);
        if (pfEnabled is not true)
            return [];

        var permission = await structureClient.GetMyHabitsPermissionAsync(cancellationToken);
        if (!HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission))
            return [];

        var approved = await repository.ListApprovedByAthleteAsync(athleteId.Value, cancellationToken);
        return approved.Select(RecommendationMapper.ToMyResponseDto).ToList();
    }
}
