using Application.DTOs.AthleteRecords;

namespace Application.UseCases.AthleteRecord.ConfirmRecordsImport;

public sealed record ConfirmRecordsImportCommand(ConfirmImportRecordsRequest Request);
