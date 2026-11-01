namespace Application.Exceptions;

public class InvitationExpiredException : Exception
{
    public InvitationExpiredException(string message = "El enlace de invitación ha expirado.")
        : base(message) { }
}

public class InvitationNotFoundException : Exception
{
    public InvitationNotFoundException(string message = "El token de invitación no existe o no es válido.")
        : base(message) { }
}

public class StructureUnavailableException : Exception
{
    public StructureUnavailableException(string message = "El servicio de Estructura no está disponible.", Exception? innerException = null)
        : base(message, innerException) { }
}