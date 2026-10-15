namespace Application.DTOs.CheckIns;

public sealed record CheckInDto(
    Guid Id,
    Guid AthleteId,
    DateTime Date,
    string TrafficLight,
    string? TrafficLightReason,
    double SleepHours,
    int SleepQuality,
    int MuscleSoreness,
    int FatigueLevel,
    int MoodRate,
    int StressLevel,
    int? EffortRate,
    bool IsSick,
    string? PainZones);