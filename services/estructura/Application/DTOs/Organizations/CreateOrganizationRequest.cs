namespace Application.DTOs.Organizations;

public sealed record CreateOrganizationRequest(
    string? Name,
    string? Description = null);
