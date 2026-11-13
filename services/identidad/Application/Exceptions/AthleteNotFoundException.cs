namespace Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando el atleta a resetear no existe, no pertenece al PF autenticado,
/// está inactivo o no posee el rol Atleta.
/// </summary>
public sealed class AthleteNotFoundException : Exception
{
    public AthleteNotFoundException(string message = "Atleta no encontrado.") : base(message)
    {
    }
}
