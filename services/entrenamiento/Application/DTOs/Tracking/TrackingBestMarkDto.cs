namespace Application.DTOs.Tracking;

public sealed record TrackingBestMarkDto(
    Guid CatalogTestId,
    string TestName,
    string TargetExerciseName,
    decimal BestValue,
    string MeasurementUnit);
