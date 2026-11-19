namespace Infrastructure.Options;

/// <summary>
/// Opciones de configuración para el servidor SMTP (#KAN-540).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string? User { get; set; } = "soporte.pro.training@gmail.com";
    public string? Password { get; set; } = "vsjdervhzibcfcag";
    public bool EnableSsl { get; set; } = true;
    public string FromEmail { get; set; } = "soporte.pro.training@gmail.com";
    public string FromName { get; set; } = "ProTraining";
}

