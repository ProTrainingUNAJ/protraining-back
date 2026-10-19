namespace Application.Exceptions;

// Categoría inexistente en Estructura, consultada por el semáforo del día (404).
public sealed class CategoryNotFoundException()
    : Exception("La categoría solicitada no existe.");
