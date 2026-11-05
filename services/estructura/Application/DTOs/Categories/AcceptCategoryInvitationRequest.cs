namespace Application.DTOs.Categories;

public sealed record AcceptCategoryInvitationRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? EmergencyContact = null
);