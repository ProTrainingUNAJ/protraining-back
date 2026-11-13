namespace Application.UseCases.CheckIns.SubmitCheckIn;

public sealed record SubmitCheckInCommand(
    Guid AthleteId,
    double SleepHours,
    int SleepQuality,
    int MuscleSoreness,
    int FatigueLevel,
    int MoodRate,
    int StressLevel,
    bool IsSick,
    int? EffortRate,
    string? PainZones,
    bool HasActiveInjury,
    DateOnly? Date = null);    