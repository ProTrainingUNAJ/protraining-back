using Application.DTOs.Athletes;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.ListAthletes;

public sealed record ListAthletesQuery;

public sealed class ListAthletesQueryHandler(IAthleteRepository repository)
{
    public async Task<IReadOnlyList<AthleteDto>> HandleAsync(ListAthletesQuery query, CancellationToken cancellationToken = default) =>
        (await repository.ListAsync(cancellationToken)).Select(AthleteMappings.ToDto).ToList();
}
