using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.GetAthlete;

public sealed record GetAthleteQuery(Guid Id);

public sealed class GetAthleteQueryHandler(IAthleteRepository repository)
{
    public async Task<AthleteDto> HandleAsync(GetAthleteQuery query, CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(query.Id, cancellationToken);
        return athlete is null ? throw new AthleteNotFoundException() : AthleteMappings.ToDto(athlete);
    }
}
