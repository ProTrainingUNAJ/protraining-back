namespace Application.DTOs.Auth;

/// <summary>
/// Modelo de entrada para el cambio de contraseña de usuario autenticado.
/// </summary>
public sealed record ChangePasswordRequestDto(string CurrentPassword, string NewPassword);

