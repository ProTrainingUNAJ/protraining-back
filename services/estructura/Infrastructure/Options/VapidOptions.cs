namespace Infrastructure.Options;

public sealed class VapidOptions
{
    public const string SectionName = "Vapid";

    public string Subject { get; set; } = "mailto:soporte@protraining.app";
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey);
}
