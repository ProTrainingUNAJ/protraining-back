namespace Application.DTOs.FitnessTests;

public sealed record AthleteFitnessTestDto(
    Guid Id,
    Guid AthleteId,
    Guid CatalogTestId,
    string TestName,
    string MotorCapacity,
    decimal Value,
    string MeasurementUnit,
    int Attempt,
    DateTime RecordedAt);

