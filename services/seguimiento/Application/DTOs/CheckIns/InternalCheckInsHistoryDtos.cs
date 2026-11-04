namespace Application.DTOs.CheckIns;

/// <summary>
/// Solicitud interna para consultar el historial de check-ins de atletas en una ventana temporal (KAN-472 · #177).
/// </summary>
public sealed record CheckInsHistoryRequest(
    IReadOnlyList<Guid> AthleteIds,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>
/// Registro histórico de check-in retornado para análisis de sobrecarga y fatiga acumulada en workers internos.
/// </summary>
public sealed record InternalCheckInHistoryDto(
    Guid Id,
    Guid AthleteId,
    DateOnly Date,
    double SleepHours,
    int SleepQuality,
    int MuscleSoreness,
    int FatigueLevel,
    int MoodRate,
    int StressLevel,
    int? EffortRate,
    bool IsSick,
    string? PainZones,
    bool HasActiveInjury);

