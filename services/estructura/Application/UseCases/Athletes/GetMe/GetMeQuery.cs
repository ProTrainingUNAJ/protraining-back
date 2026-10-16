using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;

namespace Application.UseCases.Athletes.GetMe;

public sealed record GetMeQuery;

// El atleta logueado: lo busca por la cuenta del token, así nadie puede pedir los datos de otro.
public sealed class GetMeQueryHandler(
    ICurrentUser currentUser,
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository)
{
    public async Task<MeDto> HandleAsync(GetMeQuery query, CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        // Un atleta dado de baja no tiene membresías activas.
        var categories = athlete.Active
            ? await athleteCategoryRepository.ListCategoriesByAthleteAsync(athlete.Id, cancellationToken)
            : [];

        return new MeDto(
            athlete.Id,
            athlete.FirstName,
            athlete.LastName,
            athlete.FullName,
            athlete.Active,
            categories.Select(CategoryMappings.ToDto).ToList());
    }
}
