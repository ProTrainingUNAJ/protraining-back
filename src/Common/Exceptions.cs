namespace ProTraining.Common;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class ConcurrencyException : Exception
{
    public ConcurrencyException(string message = "El registro fue modificado por otra operación. Recargue e intente nuevamente.") : base(message) { }
}
