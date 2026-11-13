namespace Application.DTOs.Auth;

/// <summary>
/// Modelo de entrada para el inicio de sesión con Google.
/// </summary>
public record GoogleLoginRequestDto(
    string IdToken,
    string? DeviceId = null
);
