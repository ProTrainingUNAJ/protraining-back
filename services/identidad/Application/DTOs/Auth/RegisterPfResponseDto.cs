namespace Application.DTOs.Auth;

/// <summary>
/// Respuesta exitosa del alta de un Preparador Físico (KAN-601).
/// </summary>
public sealed record RegisterPfResponseDto(
    Guid Id,
    string Email,
    string FullName,
    string Message
);
