namespace Application.UseCases.Benchmark;

public sealed record GetAthleteGroupBenchmarkQuery(
    Guid AthleteId,
    Guid? CategoryId = null);

