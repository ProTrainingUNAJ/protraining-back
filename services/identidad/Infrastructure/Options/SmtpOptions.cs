namespace Infrastructure.Options;

/// <summary>
/// Opciones de configuración para el servidor SMTP (#KAN-540).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "mailpit";
    public int Port { get; set; } = 1025;
    public string? User { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = false;
    public string FromEmail { get; set; } = "soporte@protraining.app";
    public string FromName { get; set; } = "ProTraining";
}

