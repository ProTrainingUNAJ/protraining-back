namespace Application.DTOs.Organizations;

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string? Description,
    int CategoriesCount = 0,
    int AthletesCount = 0,
    bool HasPhoto = false,
    DateTime? PhotoUpdatedAt = null);
