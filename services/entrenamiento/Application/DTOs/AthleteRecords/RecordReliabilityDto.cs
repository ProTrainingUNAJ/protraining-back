namespace Application.DTOs.AthleteRecords;

// Resumen de confiabilidad de los récords de una categoría (KAN-416).
public sealed record RecordReliabilityAthleteDto(Guid AthleteId, string Name);

public sealed record RecordReliabilityGroupDto(
    string Group,
    int Count,
    double Percentage,
    List<RecordReliabilityAthleteDto> Athletes
);

public sealed record RecordReliabilitySummaryDto(
    Guid CategoryId,
    Guid? ExerciseId,
    int TotalAthletes,
    List<RecordReliabilityGroupDto> Groups
);