namespace Application.DTOs.FitnessTests;

public sealed record AthleteBestMarkDto(
    Guid CatalogTestId,
    string TestName,
    string TargetExerciseName,
    decimal BestValue,
    string MeasurementUnit);
