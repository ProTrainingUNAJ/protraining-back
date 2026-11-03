using Domain.Exceptions;

namespace Domain.Services;

public static class EpleyCalculator
{
    public const int MinReps = 1;
    public const int MaxReps = 10;
    public const decimal PlateStep = 2.5m;

    /// <summary>
    /// Calcula el 1RM estimado según la fórmula de Epley: peso * (1 + repeticiones / 30).
    /// Solo es válido para 1 a 10 repeticiones. Para más de 10, devuelve null.
    /// Para peso menor o igual a 0 o repeticiones menores o iguales a 0, lanza DomainException.
    /// </summary>
    public static decimal? CalculateEstimated1Rm(decimal weight, int reps)
    {
        if (weight <= 0)
        {
            throw new DomainException("El peso debe ser mayor a 0 kg.");
        }

        if (reps <= 0)
        {
            throw new DomainException("Las repeticiones deben ser mayores a 0.");
        }

        if (reps > MaxReps)
        {
            return null;
        }

        return weight * (1m + (reps / 30m));
    }

    /// <summary>
    /// Escalón conservador: la propuesta se redondea hacia abajo al múltiplo de 2,5 kg.
    /// </summary>
    public static decimal ApplyConservativeStep(decimal weight)
    {
        return Math.Floor(weight / PlateStep) * PlateStep;
    }

    /// <summary>
    /// Calcula la propuesta de 1RM con fórmula de Epley y redondeo conservador hacia abajo a múltiplo de 2,5 kg.
    /// </summary>
    public static decimal? CalculateConservative1Rm(decimal weight, int reps)
    {
        var estimated = CalculateEstimated1Rm(weight, reps);
        if (!estimated.HasValue)
        {
            return null;
        }

        return ApplyConservativeStep(estimated.Value);
    }
}
