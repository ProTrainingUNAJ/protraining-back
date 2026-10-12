namespace Application.DTOs.Categories;

public sealed record CreateCategoryRequest(
    string? Name,
    string? Description = null);
