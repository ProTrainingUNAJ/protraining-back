namespace Application.DTOs.Auth;

/// <summary>
/// modelo de entrada para el inicio de sesion
/// </summary>
public record LoginRequestDto(string Email, string Password);
