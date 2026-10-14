using Domain.Services;
using Domain.Entities;

namespace Domain.Services;

public interface ILoadCalculatorEngine
{
    LoadResult CalculateLoad(decimal? current1Rm, RecordSource? source, decimal targetPercentage);
    decimal RoundToNearestPlate(decimal weight);
}
