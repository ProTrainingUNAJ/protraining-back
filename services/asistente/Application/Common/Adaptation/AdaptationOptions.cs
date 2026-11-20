namespace Application.Common.Adaptation;

/// <summary>
/// Configuración de parámetros para adaptación individual de rutinas (KAN-465).
/// Centraliza el escalón conservador aplicado sobre récords 1RM estimados.
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
