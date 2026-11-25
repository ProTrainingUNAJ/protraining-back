namespace Application.Exceptions;

public sealed class PhotoNotFoundException(string message = "Foto no encontrada.") : Exception(message);

