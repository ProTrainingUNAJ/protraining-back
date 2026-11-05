namespace Application.Exceptions;

public class ForbiddenException(string message = "Acceso prohibido.") : Exception(message);