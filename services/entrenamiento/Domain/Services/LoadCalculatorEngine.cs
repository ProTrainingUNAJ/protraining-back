using Domain.Entities;

namespace Domain.Services;

public class LoadCalculatorEngine : ILoadCalculatorEngine
{
    private const decimal EstimatedConservativeFactor = 0.90m;
    private const decimal PlateStep = 2.5m;

    public LoadResult CalculateLoad(decimal? current1Rm, RecordSource? source, decimal targetPercentage)
    {
        // sin record: marcar la serie como de referencia
        if (!current1Rm.HasValue || !source.HasValue || source.Value == RecordSource.NoData)
        {
            return new LoadResult(null, true);
        }

        //calculo base lineal
        decimal baseLoad = current1Rm.Value * (targetPercentage / 100m);

        //evaluar el origen del record
        decimal rawLoad = source.Value switch
        {
            RecordSource.Tested => baseLoad,
            RecordSource.Reported => baseLoad,
            RecordSource.Estimated => baseLoad * EstimatedConservativeFactor,
            _ => baseLoad
        };

        // Redondeo
        decimal finalWeight = RoundToNearestPlate(rawLoad);

        return new LoadResult(finalWeight, false);
    }
    public decimal RoundToNearestPlate(decimal weight)
    {
        return Math.Round(weight / PlateStep, MidpointRounding.AwayFromZero) * PlateStep;
    }

    public double? CalculateWeightPerSide(double? totalWeightKg, double barWeightKg = 20.0)
    {
        if (!totalWeightKg.HasValue)
            return null;

        if (totalWeightKg.Value <= barWeightKg)
            return 0.0;

        return (totalWeightKg.Value - barWeightKg) / 2.0;
    }
}