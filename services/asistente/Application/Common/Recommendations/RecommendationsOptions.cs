namespace Application.Common.Recommendations;

/// <summary>
/// Configuración del motor determinístico de recomendaciones (KAN-491).
/// Permite ajustar desde appsettings.json el umbral porcentual por debajo del promedio de la categoría.
/// </summary>
public sealed class RecommendationsOptions
{
    public const string SectionName = "Recommendations";
    public const double DefaultBelowAverageThresholdPercent = -10.0;
    public const int DefaultMaxCategoryParallelism = 4;
    public const int MaxAllowedCategoryParallelism = 16;

    /// <summary>
    /// Porcentaje por debajo del promedio de la categoría a partir del cual se dispara una recomendación.
    /// Valor por defecto: -10.0 (10 % por debajo del promedio).
    /// </summary>
    public double BelowAverageThresholdPercent { get; init; } = DefaultBelowAverageThresholdPercent;

    /// <summary>
    /// Límite máximo de atletas analizados en paralelo al correr el motor sobre una categoría (KAN-491).
    /// Valor por defecto: 4.
    /// </summary>
    public int MaxCategoryParallelism { get; init; } = DefaultMaxCategoryParallelism;

    /// <summary>
    /// Devuelve el umbral efectivo siempre con signo negativo (por ejemplo, 10.0 o -10.0 -> -10.0).
    /// Ante valores inválidos (0, NaN o infinito) cae al valor por defecto (-10.0).
    /// </summary>
    public double GetEffectiveBelowAverageThresholdPercent() =>
        NormalizeBelowAverageThreshold(BelowAverageThresholdPercent);

    /// <summary>
    /// Devuelve el grado máximo de paralelismo acotado al rango [1, 16] (cayendo al default si es menor o igual a 0).
    /// </summary>
    public int GetEffectiveMaxCategoryParallelism() =>
        MaxCategoryParallelism <= 0
            ? DefaultMaxCategoryParallelism
            : Math.Min(MaxCategoryParallelism, MaxAllowedCategoryParallelism);

    /// <summary>
    /// Normaliza cualquier valor de umbral porcentual para que sea finito, distinto de cero y negativo.
    /// </summary>
    public static double NormalizeBelowAverageThreshold(double value) =>
        !double.IsFinite(value) || value == 0
            ? DefaultBelowAverageThresholdPercent
            : -Math.Abs(value);
}
