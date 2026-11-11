namespace Infrastructure.Options;

/// <summary>
/// Opciones de configuración para la autenticación externa con Google.
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
}
