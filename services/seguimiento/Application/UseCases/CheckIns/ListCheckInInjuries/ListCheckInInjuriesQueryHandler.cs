using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Exceptions;

namespace Application.UseCases.CheckIns.ListCheckInInjuries;

public sealed record ListCheckInInjuriesQuery(Guid AthleteId);

public sealed class ListCheckInInjuriesQueryHandler(IAthleteInjuryRepository repository)
{
    public async Task<IReadOnlyList<AthleteInjuryDto>> HandleAsync(
        ListCheckInInjuriesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es inválido.");

        var injuries = await repository.ListNotClearedByAthleteAsync(query.AthleteId, cancellationToken);
        return injuries.Select(AthleteInjuryMappings.ToDto).ToList();
    }
}
