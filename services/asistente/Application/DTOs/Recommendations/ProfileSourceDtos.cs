namespace Application.DTOs.Recommendations;

/// <summary>
/// Marca individual de test físico devuelta por Seguimiento (#196 · KAN-491).
/// </summary>
public sealed record AthleteFitnessTestRemoteDto(
    Guid Id,
    Guid AthleteId,
    Guid CatalogTestId,
    string TestName,
    string MotorCapacity,
    decimal Value,
    string MeasurementUnit,
    int Attempt,
    DateTime RecordedAt);

/// <summary>
/// Promedio agregado de un test físico dentro de una categoría (KAN-447).
/// </summary>
public sealed record FitnessTestAverageRemoteDto(
    string TestName,
    string Unit,
    double Average,
    int TestedAthleteCount);

/// <summary>
/// Promedio agregado de una métrica de wellness dentro de una categoría (KAN-447).
/// </summary>
public sealed record WellnessMetricAverageRemoteDto(
    string Metric,
    double Average,
    int ReportedAthleteCount,
    int TotalCheckIns);

/// <summary>
/// Respuesta de promedios por categoría devuelta por Seguimiento (KAN-447).
/// </summary>
public sealed record CategoryAveragesRemoteDto(
    Guid CategoryId,
    int AthleteCount,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<FitnessTestAverageRemoteDto> FitnessTests,
    IReadOnlyList<WellnessMetricAverageRemoteDto> WellnessMetrics);

/// <summary>
/// Desglose promedio de métricas de bienestar semanal (KAN-489).
/// </summary>
public sealed record WellnessBreakdownRemoteDto(
    double SleepQuality,
    double SleepHours,
    double MuscleSoreness,
    double FatigueLevel,
    double MoodRate,
    double StressLevel);

/// <summary>
/// Punto semanal de tendencias de wellness y carga Foster (KAN-489).
/// </summary>
public sealed record WeeklyTrendPointRemoteDto(
    DateOnly WeekStartDate,
    DateOnly WeekEndDate,
    double? AverageWellness,
    int? TotalLoad,
    int? TotalMinutes,
    int CheckInCount,
    int LoadCount,
    WellnessBreakdownRemoteDto? WellnessBreakdown = null);

/// <summary>
/// Respuesta de tendencias semanales de un atleta devuelta por Seguimiento (KAN-489).
/// </summary>
public sealed record AthleteWeeklyTrendsRemoteDto(
    Guid AthleteId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<WeeklyTrendPointRemoteDto> Weeks);
