namespace Application.DTOs.Alerts;

/// <summary>
/// DTO de check-in histórico recibido desde el microservicio de Seguimiento (#177 · KAN-472).
/// </summary>
public sealed record AthleteCheckInHistoryItemDto(
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

