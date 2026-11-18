namespace Application.DTOs.Auth;

/// <summary>
/// Solicitud para reenviar el correo de confirmación de cuenta (KAN-601).
/// </summary>
public sealed record ResendConfirmationRequestDto(
    string Email
);

/// <summary>
/// Respuesta genérica anti-enumeración al solicitar reenvío de confirmación (KAN-601).
/// </summary>
public sealed record ResendConfirmationResponseDto(
    string Message
);
