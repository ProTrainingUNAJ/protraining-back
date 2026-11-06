namespace Application.DTOs.Training;

public sealed record TrainingRecordsResultDto(
    IReadOnlyList<AthleteRecordRemoteDto> Records,
    bool IsUnavailable
);

