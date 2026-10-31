using System.Text.Json.Serialization;

namespace Application.DTOs.AthleteRecords;

public enum ImportRecordRowStatus
{
    Ok,
    AthleteNotFound,
    ExerciseNotFound,
    InvalidWeight
}

public record ImportRecordPreviewRowDto(
    int RowNumber,
    string AthleteName,
    string ExerciseName,
    string WeightText,
    string? DateText,
    string? SourceText,
    ImportRecordRowStatus Status,
    string Message,
    Guid? AthleteId,
    Guid? ExerciseId,
    decimal? WeightOneRepMaxKg,
    DateTime? AchievedAt,
    string? Origin);

public record ConfirmImportRecordRowRequest(
    Guid AthleteId,
    Guid ExerciseId,
    decimal WeightOneRepMaxKg,
    DateTime? AchievedAt,
    string? Origin);

public record ConfirmImportRecordsRequest(List<ConfirmImportRecordRowRequest> Rows);
