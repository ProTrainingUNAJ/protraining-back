namespace Infrastructure.Services.Ai.Quota;

/// <summary>
/// Opciones de configuración para la cuota y límite diario de pedidos a la IA por preparador físico (#215 · KAN-510).
/// </summary>
public sealed class AiQuotaOptions
{
    public const string SectionName = "AiQuota";

    /// <summary>
    /// Cantidad máxima de solicitudes a modelos de IA permitidas por preparador físico por día calendario UTC.
    /// Si se supera este límite, las solicitudes subsiguientes se derivan automáticamente a reglas determinísticas.
    /// Valor por defecto: 20 pedidos diarios.
    /// </summary>
    public int DailyLimitPerPf { get; set; } = 20;

    /// <summary>
    /// Habilita o deshabilita la comprobación del límite de cuota diario.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
