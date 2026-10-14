namespace Application.Exceptions;

public sealed class StructureUnavailableException(Exception? innerException = null)
    : Exception("El servicio de Estructura no está disponible.", innerException);
