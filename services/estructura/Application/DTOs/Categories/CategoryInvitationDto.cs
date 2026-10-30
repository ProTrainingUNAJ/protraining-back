namespace Application.DTOs.Categories;

public sealed record CategoryInvitationDto(
    string Token,
    DateTime ExpiresAt,
    Guid CategoryId,
    string CategoryName,
    string? InvitationUrl = null
);