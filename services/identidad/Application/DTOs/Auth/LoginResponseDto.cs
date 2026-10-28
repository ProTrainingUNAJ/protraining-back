namespace Application.DTOs.Auth;

/// <summary>
/// respuesta devuelta tras un inicio de sesion exitoso
/// </summary>
public record LoginResponseDto(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserDto User);

