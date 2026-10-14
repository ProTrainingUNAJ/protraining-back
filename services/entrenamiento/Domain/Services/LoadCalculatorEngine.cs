using Domain.Entities;

namespace Domain.Services;

public class LoadCalculatorEngine : ILoadCalculatorEngine
{
    private const decimal EstimatedConservativeFactor = 0.95m;

    public LoadResult CalculateLoad(decimal? current1Rm, RecordSource? source, decimal targetPercentage)
    {
        // sin record: marcar la serie como de referencia
        if (!current1Rm.HasValue || !source.HasValue)
        {
            return new LoadResult(null, true);
        }

        //calculo base lineal
        decimal baseLoad = current1Rm.Value * (targetPercentage / 100m);

        //evaluar el origen del record
        return source.Value switch
        {
            RecordSource.Tested => new LoadResult(baseLoad, false),
            RecordSource.Reported => new LoadResult(baseLoad, false),

            RecordSource.Estimated => new LoadResult(baseLoad * EstimatedConservativeFactor, false), 
            _=> new LoadResult(null, true)
        };
    }
}