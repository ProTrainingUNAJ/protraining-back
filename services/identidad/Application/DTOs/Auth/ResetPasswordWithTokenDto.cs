namespace Application.DTOs.Auth;

/// <summary>
/// Solicitud para confirmar el restablecimiento de contraseña mediante token (#KAN-540).
/// </summary>
public sealed record ResetPasswordWithTokenRequestDto(string Email, string Token, string NewPassword);

/// <summary>
/// Respuesta exitosa tras restablecer la contraseña (#KAN-540).
/// </summary>
public sealed record ResetPasswordWithTokenResponseDto(string Message);

