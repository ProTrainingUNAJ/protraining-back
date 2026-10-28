namespace Domain.Common;

/// <summary>
/// Regla única de topes de carga pre-partido (KAN-407 / KAN-408 / KAN-410).
/// Día del partido (D): sin sesión de fuerza.
/// Día anterior (D-1): ningún ejercicio por encima del 60 % del 1RM.
/// Dos días antes (D-2): ningún ejercicio por encima del 75 % del 1RM.
/// </summary>
public static class MatchDayLoadRules
{
    public const bool AllowStrengthSessionOnMatchDay = false;
    public const decimal MaxPercentageOneDayBeforeMatch = 60.0m;
    public const decimal MaxPercentageTwoDaysBeforeMatch = 75.0m;

    public static decimal? GetMaxAllowedPercentage(int daysUntilMatch) => daysUntilMatch switch
    {
        1 => MaxPercentageOneDayBeforeMatch,
        2 => MaxPercentageTwoDaysBeforeMatch,
        _ => null
    };
}
