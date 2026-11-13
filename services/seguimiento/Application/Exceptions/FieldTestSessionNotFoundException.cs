namespace Application.Exceptions;

// Sesión de test de campo inexistente o perteneciente a otro PF (invisible por el
// filtro global), pedida por su id (404, KAN-425).
public sealed class FieldTestSessionNotFoundException()
    : Exception("La sesión de test solicitada no existe.");
