namespace Application.Exceptions;

// Índice único violado al guardar (p. ej. la misma sesión, prueba, atleta e intento
// cargados dos veces): el registro ya existe y no se escribió nada (409, KAN-425 / KAN-527).
public sealed class PersistenceConflictException(string message = "El registro ya existe.")
    : Exception(message);
