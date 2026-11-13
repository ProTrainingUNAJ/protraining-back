namespace Application.Exceptions;

/// <summary>
/// excepción lanzada cuando el refresh token es invalido, expiro, fue revocado o el dispositivo no coincide
/// </summary>
public sealed class InvalidRefreshTokenException() : Exception("Refresh token inválido o expirado.");
