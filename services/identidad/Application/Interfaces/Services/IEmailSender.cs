namespace Application.Interfaces.Services;

/// <summary>
/// Contrato para el despacho de correos electrónicos transaccionales (#KAN-540, KAN-601).
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envía un correo con el token de recuperación y el enlace seguro correspondiente.
    /// </summary>
    /// <param name="toEmail">Dirección de correo de destino.</param>
    /// <param name="resetToken">Token de recuperación generado por ASP.NET Core Identity.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un correo con el enlace de confirmación de cuenta para activar un nuevo Preparador Físico (KAN-601).
    /// </summary>
    /// <param name="toEmail">Dirección de correo de destino.</param>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="confirmationToken">Token de confirmación de correo.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task SendConfirmationEmailAsync(string toEmail, Guid userId, string confirmationToken, CancellationToken cancellationToken = default);
}
