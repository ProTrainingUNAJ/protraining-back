namespace Application.Exceptions;

public sealed class AiServiceUnavailableException(
    string message = "El servicio de generación de planificaciones con inteligencia artificial no se encuentra disponible. Por favor, intente más tarde.",
    Exception? innerException = null)
    : Exception(message, innerException);

