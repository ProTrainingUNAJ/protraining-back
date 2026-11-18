namespace Application.DTOs.Trends;

public sealed record WellnessMetricsBreakdownDto(
    double SleepQuality,
    double SleepHours,
    double MuscleSoreness,
    double FatigueLevel,
    double MoodRate,
    double StressLevel);

public sealed record WeeklyTrendPointDto(
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    double? AverageWellness,
    int? TotalLoad,
    int? TotalMinutes,
    int CheckInCount,
    int LoadCount,
    WellnessMetricsBreakdownDto? WellnessBreakdown = null);

public sealed record AthleteWeeklyTrendsResponseDto(
    Guid AthleteId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<WeeklyTrendPointDto> Weeks);

public sealed record CategoryWeeklyTrendPointDto(
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    double? AverageWellness,
    int? TotalLoad,
    int? TotalMinutes,
    int CheckInCount,
    int LoadCount,
    int ActiveReportingAthletes,
    WellnessMetricsBreakdownDto? WellnessBreakdown = null);

public sealed record CategoryWeeklyTrendsResponseDto(
    Guid CategoryId,
    int AthleteCount,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CategoryWeeklyTrendPointDto> Weeks);
