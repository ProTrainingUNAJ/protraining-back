namespace Application.DTOs.Attendance;

// Estado de asistencia automática del día de un atleta dentro de la categoría
// (contrato: se deriva de las cargas del día y de las correcciones manuales del PF).
public sealed record AthleteAttendanceSummaryDto(
    Guid AthleteId,
    string AthleteName,
    string Status);