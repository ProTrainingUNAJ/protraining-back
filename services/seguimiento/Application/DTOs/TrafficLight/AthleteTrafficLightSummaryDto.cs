namespace Application.DTOs.TrafficLight;

// Estado de bienestar diario del atleta para el semáforo consolidado de la categoría
// (contrato: AthleteTrafficLightSummary en contracts/seguimiento.yaml).
public sealed record AthleteTrafficLightSummaryDto(
    Guid AthleteId,
    string AthleteName,
    string TrafficLight,
    string? TrafficLightReason,
    bool HasReportedToday,
    DateTime? ReportedAt);
