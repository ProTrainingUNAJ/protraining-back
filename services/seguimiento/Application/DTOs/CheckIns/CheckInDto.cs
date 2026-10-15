namespace Application.DTOs.CheckIns;

// Check-in persistido, con el semáforo ya calculado por el motor local.
public sealed record CheckInDto(
    Guid Id,
    Guid AthleteId,
    DateOnly Date,
    string TrafficLight,
    string? TrafficLightReason,
    double SleepHours,
    int SleepQuality,
    int MuscleSoreness,
    int FatigueLevel,
    int MoodRate,
    int StressLevel,
    bool IsSick,
    int? EffortRate,
    string? PainZones,
    bool HasActiveInjury);
