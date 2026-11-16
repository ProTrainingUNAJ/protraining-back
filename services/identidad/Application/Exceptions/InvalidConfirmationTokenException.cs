namespace Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando el token de confirmación de correo es inválido o ha expirado (KAN-601).
/// </summary>
public sealed class InvalidConfirmationTokenException(string message = "El link de confirmación es inválido o ha expirado.")
    : Exception(message);
