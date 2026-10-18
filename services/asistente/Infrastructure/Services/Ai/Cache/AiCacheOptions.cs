namespace Infrastructure.Services.Ai.Cache;

/// <summary>
/// Opciones de configuración para la caché en memoria de respuestas de IA (KAN-368).
/// </summary>
public sealed class AiCacheOptions
{
    public const string SectionName = "AiCache";

    /// <summary>
    /// Habilita o deshabilita globalmente el almacenamiento en caché de respuestas.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tiempo de vida por defecto en minutos para cada elemento almacenado en caché.
    /// </summary>
    public int TtlMinutes { get; set; } = 30;
}
