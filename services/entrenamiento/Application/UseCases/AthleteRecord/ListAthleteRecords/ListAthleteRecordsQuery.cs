namespace Application.UseCases.AthleteRecord.ListAthleteRecords;

public record ListAthleteRecordsQuery(
    Guid AthleteId,
    bool? CurrentOnly = null,
    Guid? ExerciseId = null,
    DateOnly? From = null,
    DateOnly? To = null
);
