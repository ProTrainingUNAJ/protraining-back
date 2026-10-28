namespace Application.DTOs.Auth;

/// <summary>
/// respuesta con los nuevos tokens emitidos tras la renovacion
/// </summary>
public record RefreshResponseDto(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn
);
