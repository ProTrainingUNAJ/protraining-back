using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.CheckIns.ListResolvedInjuries;

public sealed class ListResolvedInjuriesQueryHandler(
    IAthleteInjuryRepository repository,
    ICheckInInjuryPainRepository injuryPainRepository)
{
    public async Task<IReadOnlyList<AthleteInjuryDto>> HandleAsync(
        ListResolvedInjuriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var injuries = await repository.ListByAthleteAsync(
            query.AthleteId,
            InjuryStatus.MedicallyCleared,
            cancellationToken);

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