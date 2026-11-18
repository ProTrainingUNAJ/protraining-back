namespace Application.DTOs.Auth;

/// <summary>
/// Solicitud de confirmación de correo para activación de cuenta (KAN-601).
/// </summary>
public sealed record ConfirmEmailRequestDto(
    Guid UserId,
    string Token
);

/// <summary>
/// Respuesta de confirmación exitosa de correo (KAN-601).
/// </summary>
public sealed record ConfirmEmailResponseDto(
    string Message
);
