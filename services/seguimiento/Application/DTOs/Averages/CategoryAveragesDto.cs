namespace Application.DTOs.Averages;

public sealed record CategoryAveragesDto(
    Guid CategoryId,
    int AthleteCount,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<FitnessTestAverageDto> FitnessTests,
    IReadOnlyList<WellnessMetricAverageDto> WellnessMetrics);

public sealed record FitnessTestAverageDto(
    string TestName,
    string Unit,
    double Average,
    int TestedAthleteCount);

public sealed record WellnessMetricAverageDto(
    string Metric,
    double Average,
    int ReportedAthleteCount,
    int TotalCheckIns);
