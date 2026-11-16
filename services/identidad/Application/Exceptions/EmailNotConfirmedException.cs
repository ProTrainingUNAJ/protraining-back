namespace Application.Exceptions;

/// <summary>
/// Excepción lanzada cuando un Preparador Físico intenta iniciar sesión sin haber confirmado su correo (KAN-601).
/// </summary>
public sealed class EmailNotConfirmedException()
    : Exception("Confirmá tu email para entrar");
