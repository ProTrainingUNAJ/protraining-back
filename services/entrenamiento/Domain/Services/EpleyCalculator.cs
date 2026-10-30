namespace Domain.Services;

public static class EpleyCalculator
{
    public const int MinReps = 1;
    public const int MaxReps = 10;
    public const decimal PlateStep = 2.5m;
    
    /// Calcula el 1RM estimado según la fórmula de Epley: peso * (1 + repeticiones / 30).
    /// Solo es válido para 1 a 10 repeticiones. Para más de 10 o menos de 1, devuelve null.

    public static decimal? CalculateConservative1Rm(decimal weight, int reps)
    {
        if (reps < MinReps || reps > MaxReps || weight <= 0)
        {
            return null;
        }

        // 1RM estimado = peso * (1 + repeticiones / 30)
        decimal estimated = weight * (1m + (reps / 30m));

        // Escalón conservador: redondeo hacia abajo al múltiplo de 2.5 kg
        return Math.Floor(estimated / PlateStep) * PlateStep;
    }
}

