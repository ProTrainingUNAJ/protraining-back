namespace Application.Exceptions;

public sealed class TrainingServiceUnavailableException(
    string message = "No se pudo crear la planificación, probá de nuevo",
    Exception? innerException = null)
    : Exception(message, innerException);
