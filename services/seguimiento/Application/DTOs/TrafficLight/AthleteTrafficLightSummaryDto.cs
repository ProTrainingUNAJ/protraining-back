namespace Application.DTOs.TrafficLight;

// Estado de bienestar diario del atleta para el semáforo consolidado de la categoría
// (contrato: AthleteTrafficLightSummary en contracts/seguimiento.yaml).
// Indicators trae el desglose del check-in para los indicadores de la card (KAN-365):
// null cuando el atleta no reportó, y ResultValue es la escala 20/50/90 del motor.
public sealed record AthleteTrafficLightSummaryDto(
    Guid AthleteId,
    string AthleteName,
    string TrafficLight,
    string? TrafficLightReason,
    bool HasReportedToday,
    DateTime? ReportedAt,
    int ResultValue,
    TrafficLightIndicatorsDto? Indicators);

// Valores crudos del check-in de hoy que alimentan los indicadores individuales
// (sueño, esfuerzo, dolor, enfermedad, lesión) dentro de la card del semáforo.
public sealed record TrafficLightIndicatorsDto(
    double SleepHours,
    int SleepQuality,
    int? EffortRate,
    bool IsSick,
    bool HasActiveInjury,
    string? PainZones);
