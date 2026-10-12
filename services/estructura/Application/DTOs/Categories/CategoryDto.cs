namespace Application.DTOs.Categories;

public sealed record CategoryDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string? Description);
