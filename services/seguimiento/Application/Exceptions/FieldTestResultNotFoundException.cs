namespace Application.Exceptions;

// Resultado inexistente o que no pertenece a la sesión pedida (o a otro PF, invisible
// por el filtro global), pedido por su id (404, KAN-425).
public sealed class FieldTestResultNotFoundException()
    : Exception("El resultado solicitado no existe.");
