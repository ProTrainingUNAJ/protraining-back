namespace Application.Exceptions;

public sealed class OrganizationNotFoundException(Guid? id = null) : Exception(id.HasValue ? $"Organización {id} no encontrada." : "Organización no encontrada.");
