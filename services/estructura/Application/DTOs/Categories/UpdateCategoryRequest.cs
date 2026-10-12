namespace Application.DTOs.Categories;

public sealed record UpdateCategoryRequest(
    string? Name,
    string? Description);
