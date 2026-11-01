namespace Application.DTOs.Benchmark;

public sealed record GroupBenchmarkMetricDto(
    string Dimension,
    double AthleteValue,
    double GroupAverage,
    string Unit,
    double Difference,
    double PercentageDifference,
    string Direction,
    string Status,
    bool IsWeakestPoint,
    string Trend);

public sealed record GroupBenchmarkResponseDto(
    Guid AthleteId,
    Guid CategoryId,
    string CategoryName,
    IReadOnlyList<GroupBenchmarkMetricDto> Metrics);

