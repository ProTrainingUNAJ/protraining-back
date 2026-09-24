using ProTraining.Common;

namespace ProTraining.Domain.Services;

/// <summary>
/// Motor de cálculo de cargas según especificaciones oficiales de ProTraining:
/// - Prescripción basada en porcentajes sobre el 1RM récord vigente.
/// - Redondeo fijo a discos de 2,5 kg (múltiplos de 2.5).
/// - Estimación de 1RM según fórmula de Epley: 1RM = Peso * (1 + Reps / 30).
/// </summary>
public static class CalculationEngine
{
    public const decimal PlateStep = 2.5m;

    /// <summary>
    /// Calcula el peso efectivo redondeado a discos de 2.5 kg.
    /// Ejemplo: 60 kg al 80% = 48 kg -> redondeado a 47.5 kg o 50 kg según cercanía a múltiplos de 2.5.
    /// En ProTraining (según CU-04/CU-05): 60 kg al 80% = 48 kg (si múltiplo exacto o redondeo más próximo).
    /// </summary>
    public static decimal CalculatePrescribedWeight(decimal oneRepMax, decimal percentage)
    {
        if (oneRepMax <= 0)
            throw new DomainException("El récord 1RM debe ser mayor a 0 para calcular la carga.");

        if (percentage <= 0 || percentage > 150)
            throw new DomainException("El porcentaje de carga debe estar entre 1% y 150%.");

        decimal rawWeight = oneRepMax * (percentage / 100m);
        return RoundToPlateStep(rawWeight);
    }

    /// <summary>
    /// Redondea un peso al incremento mínimo de disco (2.5 kg).
    /// </summary>
    public static decimal RoundToPlateStep(decimal weight)
    {
        if (weight <= 0) return 0m;
        return Math.Round(weight / PlateStep, MidpointRounding.AwayFromZero) * PlateStep;
    }

    /// <summary>
    /// Estimación de 1RM mediante fórmula de Epley: 1RM = Peso * (1 + Reps / 30).
    /// Si reps == 1, el 1RM es el peso levantado.
    /// </summary>
    public static decimal EstimateOneRepMaxEpley(decimal weightLifted, int repsCompleted)
    {
        if (weightLifted <= 0)
            throw new DomainException("El peso levantado debe ser positivo.");

        if (repsCompleted <= 0)
            throw new DomainException("Las repeticiones deben ser al menos 1.");

        if (repsCompleted == 1)
            return weightLifted;

        decimal estimated = weightLifted * (1m + (repsCompleted / 30m));
        return RoundToPlateStep(estimated);
    }
}
