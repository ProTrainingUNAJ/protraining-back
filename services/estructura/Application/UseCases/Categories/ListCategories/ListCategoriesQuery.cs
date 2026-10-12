using Application.DTOs.Categories;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Categories.ListCategories;

public sealed record ListCategoriesQuery(Guid OrganizationId);

public sealed class ListCategoriesQueryHandler(ICategoryRepository repository)
{
    public async Task<IReadOnlyList<CategoryDto>> HandleAsync(
        ListCategoriesQuery query,
        CancellationToken cancellationToken = default) =>
        (await repository.ListByOrganizationAsync(query.OrganizationId, cancellationToken))
            .Select(CategoryMappings.ToDto)
            .ToList();
}
