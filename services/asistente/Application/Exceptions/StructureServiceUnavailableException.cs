namespace Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando el microservicio de Estructura no responde o devuelve error 5xx (#128 · KAN-423).
/// Mapeada a HTTP 503 Service Unavailable en ExceptionMiddleware.
/// </summary>
public sealed class StructureServiceUnavailableException : Exception
{
    public StructureServiceUnavailableException(string message) : base(message)
    {
    }

    public StructureServiceUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

