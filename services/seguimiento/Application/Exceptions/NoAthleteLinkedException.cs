namespace Application.Exceptions;

// Cuenta válida pero sin atleta vinculado en Estructura (404).
public sealed class NoAthleteLinkedException()
    : Exception("La cuenta no tiene un atleta vinculado.");