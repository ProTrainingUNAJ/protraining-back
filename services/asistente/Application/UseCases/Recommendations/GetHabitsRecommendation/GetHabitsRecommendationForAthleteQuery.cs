using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.GenerateHabitsRecommendation;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Recommendations.GetHabitsRecommendation;

public sealed record GetHabitsRecommendationForAthleteQuery(Guid AthleteId);

/// <summary>
/// Query de lectura de la recomendación de hábitos/nutrición para validación del PF.
/// Autoriza (pertenencia, preferencia KAN-493/496), reutiliza la pendiente existente y, si no hay,
/// delega la generación/persistencia en <see cref="GenerateHabitsRecommendationCommandHandler"/>.
/// Así la Query no escribe ni llama a SaveChanges, aunque el contrato GET siga generando bajo demanda.
/// </summary>
public sealed class GetHabitsRecommendationForAthleteQueryHandler(
    IStructureAthleteClient structureClient,
    IIdentityHabitsRecommendationsClient identityClient,
    IAthleteRecommendationRepository recommendationRepository,
    GenerateHabitsRecommendationCommandHandler generateHabitsHandler)
{
    public async Task<HabitsRecommendationResponseDto> HandleAsync(
        GetHabitsRecommendationForAthleteQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var isOwnAthlete = await structureClient.IsOwnAthleteAsync(query.AthleteId, cancellationToken);
        if (!isOwnAthlete)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        // KAN-493/496: no se genera ni persiste nada si la preferencia del PF o la autorización del tutor no lo permiten.
        var pfEnabled = await identityClient.GetMyHabitsRecommendationsEnabledAsync(cancellationToken);
        var permission = await structureClient.GetHabitsPermissionAsync(query.AthleteId, cancellationToken);
        if (!HabitsRecommendationsPolicy.IsEnabled(pfEnabled, permission))
            throw new ConflictException("Las recomendaciones de hábitos y alimentación no están habilitadas para este atleta.");

        var isMinor = permission?.IsMinor == true;

        // Reutilizar recomendación pendiente existente si ya hay una para este atleta (evita duplicados innecesarios).
        var pending = await recommendationRepository.ListAsync(
            athleteId: query.AthleteId,
            status: RecommendationStatus.Pending,
            cancellationToken: cancellationToken);
        var existingPendingHabits = pending
            .Where(r => r.Type == RecommendationType.Habits)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        if (existingPendingHabits is not null)
            return RecommendationMapper.ToHabitsResponse(existingPendingHabits, isMinor);

        return await generateHabitsHandler.HandleAsync(
            new GenerateHabitsRecommendationCommand(query.AthleteId, isMinor),
            cancellationToken);
    }
}
