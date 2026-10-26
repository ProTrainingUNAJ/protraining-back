using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Injuries.ListAthleteInjuries;

// Caso de uso de lectura: solo consulta y mapea a DTO, nunca guarda cambios (AsNoTracking en el repositorio).
public sealed class ListAthleteInjuriesQueryHandler(IAthleteInjuryRepository repository)
{
    public async Task<List<AthleteInjuryDto>> HandleAsync(
        ListAthleteInjuriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var injuries = await repository.ListByAthleteAsync(query.AthleteId, query.Status, cancellationToken);

        return injuries
            .Select(AthleteInjuryMappings.ToDto)
            .ToList();
    }
}
