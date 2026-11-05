namespace Application.Exceptions;

public sealed class SeguimientoServiceUnavailableException : Exception
{
    public SeguimientoServiceUnavailableException(string message)
        : base(message)
    {
    }

    public SeguimientoServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
