using Application.DTOs.Injuries;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;

namespace Application.UseCases.Injuries.ListAthleteInjuries;

// Caso de uso de lectura: solo consulta y mapea a DTO, nunca guarda cambios (AsNoTracking en el repositorio).
public sealed class ListAthleteInjuriesQueryHandler(
    IAthleteInjuryRepository repository,
    ICheckInInjuryPainRepository injuryPainRepository,
    IStructureClient structureClient)
{
    public async Task<List<AthleteInjuryDto>> HandleAsync(
        ListAthleteInjuriesQuery query,
        CancellationToken cancellationToken = default)
    {
        // Atleta de otro PF o inexistente → 404 (KAN-431), no una lista vacía.
        if (!await structureClient.IsOwnAthleteAsync(query.AthleteId, cancellationToken))
            throw new AthleteNotFoundException();

        var injuries = await repository.ListByAthleteAsync(query.AthleteId, query.Status, cancellationToken);

        var peakPains = await injuryPainRepository.GetPeakPainByInjuryIdsAsync(
            injuries.Select(injury => injury.Id).ToList(),
            cancellationToken);

        return injuries
            .Select(injury => AthleteInjuryMappings.ToDto(
                injury,
                peakPains.TryGetValue(injury.Id, out var peak) ? peak : null))
            .ToList();
    }
}
