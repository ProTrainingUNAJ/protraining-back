namespace Application.Exceptions;

public sealed class AthleteNotFoundException(Guid? id = null) : Exception(id.HasValue ? $"Atleta {id} no encontrado." : "Atleta no encontrado.");
