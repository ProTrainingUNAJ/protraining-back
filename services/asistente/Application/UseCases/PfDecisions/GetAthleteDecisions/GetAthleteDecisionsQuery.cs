using Application.DTOs.PfDecisions;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.PfDecisions.GetAthleteDecisions;

public sealed record GetAthleteDecisionsQuery(Guid AthleteId, string? Zone = null);

public sealed class GetAthleteDecisionsQueryHandler(
    IPfDecisionRepository decisionRepository,
    IStructureAthleteClient structureAthleteClient)
{
    public async Task<IReadOnlyList<PfDecisionResponseDto>> HandleAsync(
        GetAthleteDecisionsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var isOwnAthlete = await structureAthleteClient.IsOwnAthleteAsync(query.AthleteId, cancellationToken);
        if (!isOwnAthlete)
        {
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");
        }

        var decisions = await decisionRepository.GetRecentByAthleteAsync(
            query.AthleteId,
            query.Zone,
            limit: 50,
            cancellationToken);

        return decisions
            .Select(d => new PfDecisionResponseDto(
                Id: d.Id,
                AthleteId: d.AthleteId,
                Date: d.Date,
                Source: d.Source.ToString(),
                SourceId: d.SourceId,
                Zone: d.Zone,
                SuggestedExerciseId: d.SuggestedExerciseId,
                Decision: d.Decision.ToString(),
                FinalExerciseId: d.FinalExerciseId,
                FinalPercentage: d.FinalPercentage,
                Reason: d.Reason,
                CreatedAt: d.CreatedAt
            ))
            .ToList();
    }
}

