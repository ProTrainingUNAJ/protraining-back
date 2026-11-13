namespace Application.DTOs.Attendance;

// Resumen de asistencia automática del día por categoría (contrato: CategoryAttendanceSummary
// en contracts/seguimiento.yaml). Se deduce a partir de las cargas del día cargadas por los atletas.
public sealed record CategoryAttendanceSummaryDto(
    Guid CategoryId,
    DateOnly Date,
    int TotalRoster,
    int PresentCount,
    double AttendancePercentage,
    IReadOnlyList<AthleteAttendanceSummaryDto> Athletes = default!);