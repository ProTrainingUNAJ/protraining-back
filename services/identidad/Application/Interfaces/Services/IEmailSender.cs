namespace Application.Interfaces.Services;

/// <summary>
/// Contrato para el despacho de correos electrónicos transaccionales (#KAN-540).
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
}
