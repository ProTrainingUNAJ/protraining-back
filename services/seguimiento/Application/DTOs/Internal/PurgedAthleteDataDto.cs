namespace Application.DTOs.Internal;

/// <summary>
/// Resumen de registros físicos eliminados de un atleta en Seguimiento (#204 · KAN-499).
/// </summary>
public sealed record PurgedAthleteDataDto(
    Guid AthleteId,
    int CheckInsCount,
    int DailyLoadsCount,
    int InjuriesCount,
    int FieldTestResultsCount,
    int AttendancesCount,
    int TotalRecordsPurged,
    int CheckInInjuryPainsCount = 0,
    int InjuryStatusChangesCount = 0);

