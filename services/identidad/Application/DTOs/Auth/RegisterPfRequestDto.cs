namespace Application.DTOs.Auth;

/// <summary>
/// Solicitud de registro independiente para un nuevo Preparador Físico (KAN-601).
/// </summary>
public sealed record RegisterPfRequestDto(
    string FullName,
    string Email,
    string Password,
    string? Phone = null,
    string? DeviceId = null
);
