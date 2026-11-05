namespace Application.DTOs.Evolution;

public sealed record EvolutionDataPointDto(
    DateOnly Date,
    double Value,
    string? Label = null
);

public sealed record StrengthEvolutionSeriesDto(
    Guid ExerciseId,
    string ExerciseName,
    string Unit,
    IReadOnlyList<EvolutionDataPointDto> Points
);

public sealed record FitnessTestEvolutionSeriesDto(
    Guid CatalogTestId,
    string TestName,
    string MotorCapacity,
    string Unit,
    IReadOnlyList<EvolutionDataPointDto> Points
);

public sealed record DailyWellnessPointDto(
    DateOnly Date,
    int FatigueLevel,
    double SleepHours,
    int SleepQuality,
    int MuscleSoreness,
    int MoodRate,
    int StressLevel,
    string TrafficLight,
    bool HasActiveInjury
);

public sealed record WellnessEvolutionDto(
    IReadOnlyList<EvolutionDataPointDto> FatiguePoints,
    IReadOnlyList<DailyWellnessPointDto> DailyPoints
);

public sealed record AthleteEvolutionResponseDto(
    Guid AthleteId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<StrengthEvolutionSeriesDto> Strength,
    IReadOnlyList<FitnessTestEvolutionSeriesDto> FitnessTests,
    WellnessEvolutionDto Wellness
);

