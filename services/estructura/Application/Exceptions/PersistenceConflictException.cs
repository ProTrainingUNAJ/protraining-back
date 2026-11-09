namespace Application.Exceptions;

public sealed class PersistenceConflictException(string message = "El registro ya existe.")
    : Exception(message);
