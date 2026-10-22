using Application.DTOs.AthleteRecords;

namespace Application.UseCases.AthleteRecord.RegisterAthleteRecord;

public record RegisterAthleteRecordCommand(Guid AthleteId, SaveAthleteRecordRequest Request);
