namespace Application.Exceptions;

// Índice único violado al guardar (p. ej. la misma sesión, prueba, atleta e intento
// cargados dos veces): el registro ya existe y no se escribió nada (409, KAN-425).
public sealed class PersistenceConflictException()
    : Exception("El registro ya existe.");
