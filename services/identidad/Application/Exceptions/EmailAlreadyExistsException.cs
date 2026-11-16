namespace Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando se intenta registrar un PF con un correo que ya existe en el sistema (KAN-601).
/// </summary>
public sealed class EmailAlreadyExistsException()
    : Exception("Ya existe una cuenta con ese email. Iniciá sesión");
