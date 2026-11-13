namespace Application.Exceptions;

// Lesión inexistente o perteneciente a otro PF (invisible por el filtro global), pedida por su id (404).
public sealed class InjuryNotFoundException()
    : Exception("La lesión solicitada no existe.");
