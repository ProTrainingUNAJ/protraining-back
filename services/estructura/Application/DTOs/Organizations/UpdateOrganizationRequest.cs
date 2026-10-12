namespace Application.DTOs.Organizations;

public sealed record UpdateOrganizationRequest(
    string? Name,
    string? Description);
