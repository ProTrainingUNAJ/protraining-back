using Application.Interfaces.Services;
using Infrastructure.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Infrastructure.Services;

/// <summary>
/// Implementación del servicio de correo mediante protocolo SMTP con MailKit (#KAN-540).
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["Frontend:BaseUrl"]
            ?? configuration["Frontend__BaseUrl"]
            ?? "http://localhost:5173";

        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(resetToken);
        var resetLink = $"{baseUrl.TrimEnd('/')}/reset-password?email={encodedEmail}&token={encodedToken}";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(new MailboxAddress(toEmail, toEmail));
        message.Subject = "Recuperación de contraseña - ProTraining";

        var builder = new BodyBuilder
        {
            HtmlBody = $"""
                <!DOCTYPE html>
                <html lang="es">
                <head>
                    <meta charset="utf-8" />
                    <title>Recuperar Contraseña</title>
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; color: #1e293b; padding: 24px; margin: 0;">
                    <div style="max-width: 540px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #e2e8f0; padding: 32px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);">
                        <h2 style="color: #0f172a; margin-top: 0; font-size: 20px;">Restablecé tu contraseña de ProTraining</h2>
                        <p style="font-size: 15px; line-height: 1.5; color: #475569;">
                            Recibimos una solicitud para restablecer la contraseña de tu cuenta. Hacé clic en el siguiente botón para continuar:
                        </p>
                        <div style="text-align: center; margin: 28px 0;">
                            <a href="{resetLink}" style="background-color: #0284c7; color: #ffffff; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 15px; display: inline-block;">
                                Restablecer Contraseña
                            </a>
                        </div>
                        <p style="font-size: 13px; line-height: 1.4; color: #64748b;">
                            Este enlace vencerá en <strong>30 minutos</strong> por razones de seguridad.
                        </p>
                        <p style="font-size: 12px; line-height: 1.4; color: #94a3b8; word-break: break-all; margin-top: 16px;">
                            Si el botón no funciona, copiá y pegá el siguiente enlace en tu navegador:<br />
                            <a href="{resetLink}" style="color: #0284c7;">{resetLink}</a>
                        </p>
                        <hr style="border: 0; border-top: 1px solid #f1f5f9; margin: 24px 0;" />
                        <p style="font-size: 12px; color: #94a3b8; margin-bottom: 0;">
                            Si vos no solicitaste este cambio, podés ignorar este correo; tu contraseña actual continuará siendo la misma.
                        </p>
                    </div>
                </body>
                </html>
                """,
            TextBody = $"""
                Recuperación de contraseña - ProTraining

                Recibimos una solicitud para restablecer la contraseña de tu cuenta.
                Ingresá al siguiente enlace para definir tu nueva clave:

                {resetLink}

                Este enlace vencerá en 30 minutos.
                Si vos no solicitaste este cambio, ignorá este mensaje.
                """
        };

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 5000;

        try
        {
            var security = _options.EnableSsl
                ? SecureSocketOptions.Auto
                : SecureSocketOptions.None;

            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.User) && !string.IsNullOrWhiteSpace(_options.Password))
            {
                await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Correo de recuperación enviado con éxito.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al enviar correo SMTP a través de {Host}:{Port}.", _options.Host, _options.Port);
            throw;
        }
    }
}
