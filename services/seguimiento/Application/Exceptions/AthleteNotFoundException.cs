namespace Application.Exceptions;

// Atleta inexistente o de otro PF (404, KAN-431): nunca 403, para no revelar que el id existe.
public sealed class AthleteNotFoundException()
    : Exception("El atleta no existe o no es tuyo.");
