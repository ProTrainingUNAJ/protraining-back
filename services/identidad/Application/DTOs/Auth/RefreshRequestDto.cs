namespace Application.DTOs.Auth;
/// <summary>
/// credenciales requeridas para renovar el token de acceso
/// </summary>
public record RefreshRequestDto(
    string RefreshToken,
    string DeviceId
);
