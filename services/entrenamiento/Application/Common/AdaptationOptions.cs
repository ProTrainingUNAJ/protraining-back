namespace Application.Common;

/// <summary>
/// Configuración compartida para el cálculo conservador de cargas sobre récords 1RM estimados (KAN-465).
/// Usa la misma clave <c>Adaptation:EstimatedRecordConservativePercent</c> que el servicio Asistente
/// para mantener sincronizados el cálculo real de kilos y lo mostrado en pantalla.
/// </summary>
public sealed class AdaptationOptions
{
    public const string SectionName = "Adaptation";
    public const decimal DefaultEstimatedRecordConservativePercent = 90m;

    /// <summary>
    /// Porcentaje conservador utilizado cuando el 1RM del atleta es estimado y no testeado (por defecto 90 %).
    /// </summary>
    public decimal EstimatedRecordConservativePercent { get; init; } = DefaultEstimatedRecordConservativePercent;

    public decimal GetEffectiveEstimatedRecordConservativePercent() =>
        EstimatedRecordConservativePercent is > 0m and <= 100m
            ? EstimatedRecordConservativePercent
            : DefaultEstimatedRecordConservativePercent;
}
