namespace Application.UseCases.AthleteRecord.PreviewRecordsImport;

public sealed record PreviewRecordsImportQuery(
    string CsvContent,
    string AthleteColumn,
    string ExerciseColumn,
    string WeightColumn,
    string? DateColumn,
    string? SourceColumn);
