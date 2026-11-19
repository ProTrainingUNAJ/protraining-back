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
/// Implementación del servicio de correo mediante protocolo SMTP con MailKit (#KAN-540, KAN-601).
/// Soporta marca ProTraining, colores institucionales y logo embebido (#KAN-602).
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    private static void AttachLogoIfAvailable(BodyBuilder builder)
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "services", "identidad", "Assets", "logo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "services", "identidad", "API", "Assets", "logo.png"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "protraining-front", "brand", "logo-horizontal.png"),
        };

        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    var image = builder.LinkedResources.Add(path);
                    image.ContentId = "protraining-logo";
                    image.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
                    break;
                }
            }
            catch
            {
                // Si falla la vinculación del archivo, continuamos con el renderizado HTML de respaldo
            }
        }
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["Frontend:BaseUrl"]
            ?? configuration["Frontend__BaseUrl"]
            ?? "http://127.0.0.1:5173";

        var encodedEmail = Uri.EscapeDataString(toEmail);
        var encodedToken = Uri.EscapeDataString(resetToken);
        var resetLink = $"{baseUrl.TrimEnd('/')}/reset-password?email={encodedEmail}&token={encodedToken}";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(new MailboxAddress(toEmail, toEmail));
        message.Subject = "Restablecé tu contraseña - ProTraining";

        var builder = new BodyBuilder();
        AttachLogoIfAvailable(builder);

        builder.HtmlBody = $"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Restablecer Contraseña - ProTraining</title>
            </head>
            <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #F8FAFC; color: #0F172A; padding: 32px 16px; margin: 0;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                    <tr>
                        <td align="center">
                            <div style="max-width: 520px; width: 100%; background-color: #FFFFFF; border-radius: 16px; border: 1px solid #E2E8F0; overflow: hidden; box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.05);">
                                <!-- Barra de acento con gradiente institucional -->
                                <div style="height: 5px; background: linear-gradient(90deg, #0066FF 0%, #00C2FF 100%);"></div>
                                
                                <div style="padding: 36px 32px;">
                                    <!-- Logo o Isotipo Oficial -->
                                    <div style="text-align: center; margin-bottom: 28px;">
                                        <img src="cid:protraining-logo" alt="PROTRAINING" width="190" style="max-width: 190px; height: auto; display: inline-block; border: 0; outline: none;" />
                                    </div>

                                    <h1 style="color: #0A2540; margin-top: 0; margin-bottom: 14px; font-size: 21px; font-weight: 800; text-align: center; letter-spacing: -0.02em;">
                                        Restablecé tu contraseña
                                    </h1>
                                    
                                    <p style="font-size: 15px; line-height: 1.6; color: #475569; text-align: center; margin-top: 0; margin-bottom: 26px;">
                                        Recibimos una solicitud para definir una nueva contraseña en tu cuenta de <strong>ProTraining</strong>. Hacé clic en el siguiente botón para continuar:
                                    </p>

                                    <!-- Botón Principal (Pill) -->
                                    <div style="text-align: center; margin: 0 0 28px 0;">
                                        <a href="{resetLink}" target="_blank" style="background-color: #0066FF; color: #FFFFFF; padding: 14px 32px; border-radius: 9999px; text-decoration: none; font-weight: 700; font-size: 15px; display: inline-block; box-shadow: 0 4px 14px rgba(0, 102, 255, 0.35);">
                                            Restablecer Contraseña
                                        </a>
                                    </div>

                                    <!-- Cuadro de Seguridad Informativo -->
                                    <div style="background-color: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 10px; padding: 14px 18px; margin-bottom: 24px; text-align: left;">
                                        <p style="font-size: 13px; line-height: 1.5; color: #64748B; margin: 0;">
                                            ⏱️ Este enlace de seguridad vencerá en <strong>30 minutos</strong>. Si vos no solicitaste este cambio, podés ignorar este correo; tu clave seguirá siendo la misma.
                                        </p>
                                    </div>

                                    <!-- Enlace en Texto Plano -->
                                    <p style="font-size: 12px; line-height: 1.5; color: #94A3B8; word-break: break-all; margin: 0 0 24px 0; text-align: center;">
                                        Si el botón no funciona, copiá y pegá este enlace en tu navegador:<br />
                                        <a href="{resetLink}" style="color: #0066FF; text-decoration: underline;">{resetLink}</a>
                                    </p>

                                    <!-- Pie de Página Institucional -->
                                    <div style="border-top: 1px solid #F1F5F9; padding-top: 20px; text-align: center;">
                                        <p style="font-size: 12px; color: #94A3B8; margin: 0; line-height: 1.5;">
                                            <strong>ProTraining</strong> · Plataforma integral de gestión y rendimiento deportivo<br />
                                            <a href="mailto:soporte.pro.training@gmail.com" style="color: #94A3B8; text-decoration: none;">soporte.pro.training@gmail.com</a>
                                        </p>
                                    </div>
                                </div>
                            </div>
                        </td>
                    </tr>
                </table>
            </body>
            </html>
            """;

        builder.TextBody = $"""
            Restablecer contraseña - ProTraining

            Recibimos una solicitud para definir una nueva contraseña en tu cuenta.
            Ingresá al siguiente enlace para continuar:

            {resetLink}

            Este enlace vencerá en 30 minutos.
            Si no solicitaste este cambio, podés ignorar este mensaje.
            """;

        message.Body = builder.ToMessageBody();

        await ExecuteSmtpSendAsync(message, toEmail, cancellationToken);
    }

    public async Task SendConfirmationEmailAsync(string toEmail, Guid userId, string confirmationToken, CancellationToken cancellationToken = default)
    {
        var baseUrl = configuration["Frontend:BaseUrl"]
            ?? configuration["Frontend__BaseUrl"]
            ?? "http://127.0.0.1:5173";

        var encodedToken = Uri.EscapeDataString(confirmationToken);
        var encodedEmail = Uri.EscapeDataString(toEmail);
        var confirmLink = $"{baseUrl.TrimEnd('/')}/confirmar-email?userId={userId}&token={encodedToken}&email={encodedEmail}";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(new MailboxAddress(toEmail, toEmail));
        message.Subject = "Confirmá tu cuenta de Preparador Físico - ProTraining";

        var builder = new BodyBuilder();
        AttachLogoIfAvailable(builder);

        builder.HtmlBody = $"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Confirmá tu cuenta de Preparador Físico - ProTraining</title>
            </head>
            <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #F8FAFC; color: #0F172A; padding: 32px 16px; margin: 0;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                    <tr>
                        <td align="center">
                            <div style="max-width: 520px; width: 100%; background-color: #FFFFFF; border-radius: 16px; border: 1px solid #E2E8F0; overflow: hidden; box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.05);">
                                <!-- Barra de acento con gradiente institucional -->
                                <div style="height: 5px; background: linear-gradient(90deg, #0066FF 0%, #00C2FF 100%);"></div>
                                
                                <div style="padding: 36px 32px;">
                                    <!-- Logo o Isotipo Oficial -->
                                    <div style="text-align: center; margin-bottom: 28px;">
                                        <img src="cid:protraining-logo" alt="PROTRAINING" width="190" style="max-width: 190px; height: auto; display: inline-block; border: 0; outline: none;" />
                                    </div>

                                    <h1 style="color: #0A2540; margin-top: 0; margin-bottom: 14px; font-size: 21px; font-weight: 800; text-align: center; letter-spacing: -0.02em;">
                                        ¡Bienvenido a ProTraining!
                                    </h1>
                                    
                                    <p style="font-size: 15px; line-height: 1.6; color: #475569; text-align: center; margin-top: 0; margin-bottom: 26px;">
                                        Para activar tu cuenta de <strong>Preparador Físico</strong> y comenzar a gestionar a tus planteles y atletas, por favor confirmá tu correo electrónico haciendo clic en el siguiente botón:
                                    </p>

                                    <!-- Botón Principal (Pill) -->
                                    <div style="text-align: center; margin: 0 0 28px 0;">
                                        <a href="{confirmLink}" target="_blank" style="background-color: #0066FF; color: #FFFFFF; padding: 14px 32px; border-radius: 9999px; text-decoration: none; font-weight: 700; font-size: 15px; display: inline-block; box-shadow: 0 4px 14px rgba(0, 102, 255, 0.35);">
                                            Confirmar mi cuenta
                                        </a>
                                    </div>

                                    <!-- Cuadro de Seguridad Informativo -->
                                    <div style="background-color: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 10px; padding: 14px 18px; margin-bottom: 24px; text-align: left;">
                                        <p style="font-size: 13px; line-height: 1.5; color: #64748B; margin: 0;">
                                            ⏱️ Este enlace de confirmación vencerá en <strong>24 horas</strong>. Si vos no creaste esta cuenta, podés ignorar este correo sin ningún inconveniente.
                                        </p>
                                    </div>

                                    <!-- Enlace en Texto Plano -->
                                    <p style="font-size: 12px; line-height: 1.5; color: #94A3B8; word-break: break-all; margin: 0 0 24px 0; text-align: center;">
                                        Si el botón no funciona, copiá y pegá este enlace en tu navegador:<br />
                                        <a href="{confirmLink}" style="color: #0066FF; text-decoration: underline;">{confirmLink}</a>
                                    </p>

                                    <!-- Pie de Página Institucional -->
                                    <div style="border-top: 1px solid #F1F5F9; padding-top: 20px; text-align: center;">
                                        <p style="font-size: 12px; color: #94A3B8; margin: 0; line-height: 1.5;">
                                            <strong>ProTraining</strong> · Plataforma integral de gestión y rendimiento deportivo<br />
                                            <a href="mailto:soporte.pro.training@gmail.com" style="color: #94A3B8; text-decoration: none;">soporte.pro.training@gmail.com</a>
                                        </p>
                                    </div>
                                </div>
                            </div>
                        </td>
                    </tr>
                </table>
            </body>
            </html>
            """;

        builder.TextBody = $"""
            ¡Bienvenido a ProTraining!

            Para activar tu cuenta de Preparador Físico, ingresá al siguiente enlace para confirmar tu correo:

            {confirmLink}

            Este enlace vencerá en 24 horas.
            Si no solicitaste esta cuenta, ignorá este mensaje.
            """;

        message.Body = builder.ToMessageBody();

        await ExecuteSmtpSendAsync(message, toEmail, cancellationToken);
    }

    private async Task ExecuteSmtpSendAsync(MimeMessage message, string toEmail, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();
        client.Timeout = 15000;

        try
        {
            var security = _options.EnableSsl
                ? (_options.Port == 587
                    ? SecureSocketOptions.StartTls
                    : (_options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto))
                : SecureSocketOptions.None;

            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

            var password = _options.Password?.Replace(" ", "");
            if (!string.IsNullOrWhiteSpace(_options.User) && !string.IsNullOrWhiteSpace(password))
            {
                await client.AuthenticateAsync(_options.User, password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Correo enviado con éxito a {ToEmail} a través de {Host}:{Port}.", toEmail, _options.Host, _options.Port);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al enviar correo SMTP a {ToEmail} a través de {Host}:{Port}.", toEmail, _options.Host, _options.Port);
            throw;
        }
    }
}
