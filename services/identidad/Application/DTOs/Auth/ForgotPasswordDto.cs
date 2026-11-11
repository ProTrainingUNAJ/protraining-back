namespace Application.DTOs.Auth;

/// <summary>
/// Solicitud pública para iniciar la recuperación de contraseña (#KAN-540).
/// </summary>
public sealed record ForgotPasswordRequestDto(string Email);

/// <summary>
/// Respuesta genérica anti-enumeración de cuentas (#KAN-540).
/// </summary>
public sealed record ForgotPasswordResponseDto(string Message);

