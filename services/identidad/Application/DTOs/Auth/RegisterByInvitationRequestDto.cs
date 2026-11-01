namespace Application.DTOs.Auth;

/// <summary>
/// Modelo de entrada para el registro de un atleta mediante token de invitación (KAN-397).
/// </summary>
public record RegisterByInvitationRequestDto(
    string Email,
    string Password,
    string FullName,
    string Token,
    string? DeviceId = null
);