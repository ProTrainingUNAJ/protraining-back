namespace Application.Exceptions;

public sealed class PersistenceConcurrencyException() : Exception("Conflicto de concurrencia.");
