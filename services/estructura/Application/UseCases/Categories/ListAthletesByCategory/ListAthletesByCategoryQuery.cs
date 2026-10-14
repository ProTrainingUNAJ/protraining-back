using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Categories.ListAthletesByCategory;

public sealed record ListAthletesByCategoryQuery(Guid CategoryId);

public sealed class ListAthletesByCategoryQueryHandler(
    ICategoryRepository categoryRepository,
    IAthleteCategoryRepository athleteCategoryRepository)
{
    public async Task<IReadOnlyList<AthleteDto>> HandleAsync(
        ListAthletesByCategoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetByIdAsync(query.CategoryId, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        var athletes = await athleteCategoryRepository.ListAthletesByCategoryAsync(query.CategoryId, cancellationToken);
        return athletes.Select(AthleteMappings.ToDto).ToList();
    }
}
