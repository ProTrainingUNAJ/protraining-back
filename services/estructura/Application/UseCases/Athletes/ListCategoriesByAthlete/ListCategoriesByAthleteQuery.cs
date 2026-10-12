using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.ListCategoriesByAthlete;

public sealed record ListCategoriesByAthleteQuery(Guid AthleteId);

public sealed class ListCategoriesByAthleteQueryHandler(
    IAthleteRepository athleteRepository,
    IAthleteCategoryRepository athleteCategoryRepository)
{
    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(
        ListCategoriesByAthleteQuery query,
        CancellationToken cancellationToken = default)
    {
        var athlete = await athleteRepository.GetByIdAsync(query.AthleteId, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        var categories = await athleteCategoryRepository.ListCategoriesByAthleteAsync(query.AthleteId, cancellationToken);
        return categories.Select(CategoryMappings.ToDto).ToList();
    }
}
