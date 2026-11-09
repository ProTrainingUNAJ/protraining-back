using Application.DTOs.Adaptation;
using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.UseCases.Adaptation.GetAthleteAdaptationContext;

public sealed record GetAthleteAdaptationContextQuery(Guid AthleteId);

public sealed class GetAthleteAdaptationContextQueryHandler(
    IAthleteAdaptationContextService contextService,
    IStructureAthleteClient structureAthleteClient)
{
    public async Task<AthleteAdaptationContextDto> HandleAsync(
        GetAthleteAdaptationContextQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        if (!await structureAthleteClient.IsOwnAthleteAsync(query.AthleteId, cancellationToken))
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        return await contextService.GetAsync(query.AthleteId, cancellationToken);
    }
}
