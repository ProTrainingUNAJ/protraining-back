namespace Application.DTOs.Dashboard;

// Presentismo agregado de una categoría en un período. "EvaluatedDays" son los días
// del rango con algún registro (carga o corrección) en la categoría: no existe un
// modelo de sesiones programadas, así que esos días son la referencia del cálculo.
public sealed record CategoryAttendanceMetricsDto(
    int PresentDays,
    int AbsentDays,
    int WithoutRecordDays,
    int EvaluatedAthleteDays,
    double AttendancePercentage);

// Solo conteos: la información médica individual no sale del servicio (KAN-504).
public sealed record CategoryInjuryMetricsDto(
    int ActiveCount,
    int InRecoveryCount,
    int ClearedInPeriodCount,
    int AthletesAffected);

public sealed record CategoryDashboardLoadWeekDto(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int TotalLoad,
    int TotalMinutes,
    int RecordsCount);

public sealed record CategoryLoadMetricsDto(
    int TotalLoad,
    int TotalMinutes,
    int RecordsCount,
    IReadOnlyList<CategoryDashboardLoadWeekDto> Weeks);

public sealed record CategoryDashboardSummaryDto(
    Guid CategoryId,
    DateOnly From,
    DateOnly To,
    int AthleteCount,
    int EvaluatedDays,
    CategoryAttendanceMetricsDto Attendance,
    CategoryInjuryMetricsDto Injuries,
    CategoryLoadMetricsDto Load);
