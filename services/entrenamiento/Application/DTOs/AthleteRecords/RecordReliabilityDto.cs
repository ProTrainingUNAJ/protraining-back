namespace Application.DTOs.AthleteRecords;

// Resumen de confiabilidad de los récords de una categoría (KAN-416).
public sealed record RecordReliabilityAthleteDto(Guid AthleteId, string Name, int RecordCount);

public sealed record RecordReliabilityGroupDto(
    string Group,
    int AthleteCount,
    int RecordCount,
    double Percentage,
    List<RecordReliabilityAthleteDto> Athletes
);

public sealed record RecordReliabilitySummaryDto(
    Guid CategoryId,
    Guid? ExerciseId,
    int TotalAthletes,
    int TotalRecords,
    List<RecordReliabilityGroupDto> Groups
);
