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
        var category = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (category is null)
            throw new CategoryNotFoundException();

        return CategoryMappings.ToDto(category);
    }
}
