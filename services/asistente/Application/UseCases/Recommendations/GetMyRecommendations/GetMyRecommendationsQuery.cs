using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.Recommendations.GetMyRecommendations;

public sealed record GetMyRecommendationsQuery;

/// <summary>
/// Devuelve las recomendaciones aprobadas por el PF para el atleta autenticado (#197 · KAN-492).
/// El identificador de atleta siempre sale del token a través de Estructura GET /api/v1/me.
/// El interruptor del PF y la autorización del tutor solo restringen las recomendaciones de
/// hábitos y alimentación. Las de entrenamiento aprobadas se devuelven siempre, incluso con el
/// interruptor apagado o sin comunicación con Identidad/Estructura (fail-closed solo para hábitos).
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

        var approved = await repository.ListApprovedByAthleteAsync(athleteId.Value, cancellationToken);

        var pfEnabled = await identityClient.GetAthletePfHabitsRecommendationsEnabledAsync(cancellationToken);
        var permission = pfEnabled is true
            ? await structureClient.GetMyHabitsPermissionAsync(cancellationToken)
            : null;

        var allowHabits = HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission);
        var visible = allowHabits
            ? approved
            : approved.Where(r => r.Type == RecommendationType.Training);

        return visible.Select(RecommendationMapper.ToMyResponseDto).ToList();
    }
}
