using Application.DTOs.Categories;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Categories.GetCategory;

public sealed record GetCategoryQuery(Guid Id);

public sealed class GetCategoryQueryHandler(ICategoryRepository repository)
{
    public async Task<CategoryDto> HandleAsync(
        GetCategoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var withCounts = await repository.GetWithAthletesCountByIdAsync(query.Id, cancellationToken);
        if (withCounts is null)
            throw new CategoryNotFoundException();

        return CategoryMappings.ToDto(withCounts.Value.Category, withCounts.Value.AthletesCount);
    }
}
