namespace Application.DTOs.Auth;

/// <summary>
/// Solicitud interna de validación de sesión para el API Gateway (#KAN-539).
/// </summary>
public record ValidateSessionRequestDto(
    Guid UserId,
    string SecurityStamp);

/// <summary>
/// Respuesta interna con el estado de validez de la sesión del usuario.
/// </summary>
public record ValidateSessionResponseDto(
    bool IsValid,
    bool MustChangePassword,
    string? FailureReason = null);
