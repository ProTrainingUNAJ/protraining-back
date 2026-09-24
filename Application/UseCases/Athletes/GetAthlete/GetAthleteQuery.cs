using Application.DTOs;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Athletes.GetAthlete;

public sealed record GetAthleteQuery(long Id);

public sealed class GetAthleteQueryHandler(IAthleteRepository repository)
{
    public async Task<AthleteDto> HandleAsync(GetAthleteQuery query, CancellationToken cancellationToken = default)
    {
        Athlete.ValidateId(query.Id);
        var athlete = await repository.GetByIdAsync(query.Id, cancellationToken);
        return athlete is null ? throw new AthleteNotFoundException() : AthleteMappings.ToDto(athlete);
    }
}
