namespace Application.Exceptions;

/// <summary>
/// Lanzada cuando el email ya existe como Atleta (Regla KAN-397: HTTP 409 con 'Ya tenés cuenta, iniciá sesión').
/// </summary>
public class AthleteAlreadyExistsException : Exception
{
    public AthleteAlreadyExistsException(string message = "Ya tenés cuenta, iniciá sesión.")
        : base(message) { }
}