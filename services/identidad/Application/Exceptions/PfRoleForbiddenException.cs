namespace Application.Exceptions;

/// <summary>
/// Lanzada cuando el email ya existe con rol PreparadorFisico (Regla KAN-397: HTTP 403 y no se crea nada).
/// </summary>
public class PfRoleForbiddenException : Exception
{
    public PfRoleForbiddenException(string message = "No se permite el registro. Los usuarios con rol Preparador Físico no pueden registrarse mediante invitación de atleta.")
        : base(message) { }
}