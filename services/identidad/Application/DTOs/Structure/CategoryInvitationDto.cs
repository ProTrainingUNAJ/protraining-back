namespace Application.DTOs.Structure;

public sealed record CategoryInvitationDto(
    string Token,
    DateTime ExpiresAt,
    Guid CategoryId,
    string CategoryName
);