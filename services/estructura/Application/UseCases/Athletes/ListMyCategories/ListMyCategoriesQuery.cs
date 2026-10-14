using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;

namespace Application.UseCases.Athletes.ListMyCategories;

public sealed record ListMyCategoriesQuery;

// Categorías del atleta logueado: lo busca por la cuenta del token, así solo ve las suyas.
public sealed class ListMyCategoriesQueryHandler(
    ICurrentUser currentUser,
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository)
{
    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(
        ListMyCategoriesQuery query,
        CancellationToken cancellationToken = default)
    {
        var accountId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var athlete = await athleteRepository.GetByAccountIdAsync(accountId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        // Un atleta dado de baja no tiene membresías activas.
        if (!athlete.Active)
            return [];

        var categories = await athleteCategoryRepository.ListCategoriesByAthleteAsync(athlete.Id, cancellationToken);
        return categories.Select(CategoryMappings.ToDto).ToList();
    }
}
