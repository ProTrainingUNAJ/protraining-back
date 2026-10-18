namespace Application.DTOs.Categories;

public sealed record CreateCategoryRequest(
    Guid OrganizationId,
    string? Name,
    string? Description = null);
