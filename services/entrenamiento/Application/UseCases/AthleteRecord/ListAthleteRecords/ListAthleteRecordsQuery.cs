namespace Application.UseCases.AthleteRecord.ListAthleteRecords;

public record ListAthleteRecordsQuery(Guid AthleteId, bool CurrentOnly = true);
