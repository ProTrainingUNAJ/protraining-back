namespace Application.Exceptions;

/// <summary>
/// excepcion lanzada cuando las credenciales enviadas son incorrectas o la cuenta está bloqueada.
/// </summary>
public sealed class InvalidCredentialsException() : Exception("Email o contraseña incorrectos.");
