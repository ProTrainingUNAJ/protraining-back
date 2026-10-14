using Application.DTOs.Categories;
using Domain.Entities;

namespace Application.Mappings;

public static class CategoryMappings
{
    public static CategoryDto ToDto(Category category) =>
        new(category.Id, category.OrganizationId, category.Name, category.Description);
}
