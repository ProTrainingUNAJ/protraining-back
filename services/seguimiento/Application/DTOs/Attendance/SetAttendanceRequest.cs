namespace Application.DTOs.Attendance;

// Corrección manual de asistencia por parte del PF (KAN-445).
public sealed record SetAttendanceRequest(bool Present);