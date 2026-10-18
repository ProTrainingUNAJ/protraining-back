namespace Application.Exceptions;

// El token no corresponde al atleta de la ruta (403).
public sealed class ForbiddenException(string message = "La cuenta del token no corresponde al atleta solicitado.")
    : Exception(message);