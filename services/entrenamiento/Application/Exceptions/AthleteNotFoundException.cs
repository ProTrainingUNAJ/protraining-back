namespace Application.Exceptions;

// Atleta inexistente o de otro PF (404, KAN-431/KAN-434): nunca 403, para no revelar la existencia del atleta.
public sealed class AthleteNotFoundException : NotFoundException
{
    public AthleteNotFoundException(Guid? athleteId = null)
        : base(athleteId.HasValue
            ? $"No se encontró el atleta con ID '{athleteId.Value}'."
            : "El atleta no existe o no pertenece al preparador físico.")
    {
    }
}

