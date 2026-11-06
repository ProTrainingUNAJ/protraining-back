namespace Application.Exceptions;

// Prueba del catálogo inexistente o de otro PF (invisible por el filtro global),
// referenciada por un resultado de sesión (404, KAN-426).
public sealed class CatalogTestNotFoundException()
    : Exception("La prueba del catálogo solicitada no existe.");
