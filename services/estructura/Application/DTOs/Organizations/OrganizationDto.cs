namespace Application.DTOs.Organizations;

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string? Description);
