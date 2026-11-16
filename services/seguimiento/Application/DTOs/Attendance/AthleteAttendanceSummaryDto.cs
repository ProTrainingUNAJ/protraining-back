namespace Application.DTOs.Attendance;

public sealed record AttendanceActivityDetailDto(
    string Type,
    string Name,
    int Minutes,
    int? Rpe);

// Estado de asistencia automática del día de un atleta dentro de la categoría
// (contrato: se deriva de las cargas del día y de las correcciones manuales del PF).
public sealed record AthleteAttendanceSummaryDto(
    Guid AthleteId,
    string AthleteName,
    string Status,
    int TotalMinutes = 0,
    int TotalLoad = 0,
    IReadOnlyList<AttendanceActivityDetailDto>? Activities = null);
