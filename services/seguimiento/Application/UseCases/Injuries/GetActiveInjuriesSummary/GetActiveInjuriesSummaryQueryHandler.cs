using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Domain.Exceptions;

namespace Application.UseCases.Injuries.GetActiveInjuriesSummary;

/// <summary>
/// Devuelve el conteo de lesiones activas (estado Active) por atleta en una sola consulta (KAN-464).
/// El aislamiento por PfId y borrado lógico lo aplica automáticamente el filtro global de AppDbContext.
/// </summary>
public sealed class GetActiveInjuriesSummaryQueryHandler(IAthleteInjuryRepository repository)
{
    public const int MaxAthleteIdsPerRequest = 100;

    public async Task<IReadOnlyList<AthleteActiveInjurySummaryDto>> HandleAsync(
        GetActiveInjuriesSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteIds is null || query.AthleteIds.Count == 0)
            return [];

        if (query.AthleteIds.Count > MaxAthleteIdsPerRequest)
        {
            throw new DomainException(
                $"Se pueden consultar hasta {MaxAthleteIdsPerRequest} atletas por solicitud.");
        }

        var distinctIds = query.AthleteIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctIds.Count == 0)
            return [];

        var countsByAthlete = await repository.CountActiveByAthleteIdsAsync(distinctIds, cancellationToken);

        return distinctIds
            .Select(athleteId => new AthleteActiveInjurySummaryDto(
                athleteId,
                countsByAthlete.GetValueOrDefault(athleteId, 0)))
            .ToList();
    }
}
