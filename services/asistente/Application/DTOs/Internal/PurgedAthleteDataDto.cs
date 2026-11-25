namespace Application.DTOs.Internal;

/// <summary>
/// Resumen de registros físicos eliminados de un atleta en Asistente (#205 · KAN-500).
/// </summary>
public sealed record PurgedAthleteDataDto(
    Guid AthleteId,
    int RoutineDraftsCount,
    int PlanDraftsCount,
    int DayAdjustmentsCount,
    int AthleteRecommendationsCount,
    int PreventiveAlertsCount,
    int PfDecisionsCount,
    int AiLogsCount,
    int TotalRecordsPurged);
