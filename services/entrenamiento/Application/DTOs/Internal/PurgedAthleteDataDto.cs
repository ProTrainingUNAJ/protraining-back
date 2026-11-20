namespace Application.DTOs.Internal;

/// <summary>
/// Resumen de registros físicos eliminados de un atleta en Entrenamiento (#204 · KAN-499).
/// </summary>
public sealed record PurgedAthleteDataDto(
    Guid AthleteId,
    int PerformedSetsCount,
    int RecordProposalsCount,
    int AthleteRecordsCount,
    int SessionAdjustmentsCount,
    int PlanAssignmentsCount,
    int AthleteVariantsCount,
    int TotalRecordsPurged);

