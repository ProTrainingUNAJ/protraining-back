namespace Application.DTOs.Users;

/// <summary>
/// Solicitud de actualización de perfil para el Preparador Físico (KAN-601).
/// </summary>
public sealed record UpdateProfileRequestDto(
    string FullName,
    string? Phone = null
);

/// <summary>
/// Respuesta de actualización de perfil de usuario (KAN-601).
/// </summary>
public sealed record ProfileResponseDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber
);

/// <summary>
/// Solicitud de baja lógica de la cuenta del Preparador Físico (KAN-601).
/// </summary>
public sealed record DeactivateAccountRequestDto(
    string Password
);
