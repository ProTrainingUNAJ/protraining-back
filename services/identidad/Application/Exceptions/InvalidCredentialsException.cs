namespace Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando las credenciales enviadas son incorrectas, inválidas o la cuenta no está autorizada.
/// </summary>
public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Email o contraseña incorrectos.") { }

    public InvalidCredentialsException(string message) : base(message) { }
}
