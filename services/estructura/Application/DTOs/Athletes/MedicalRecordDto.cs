namespace Application.DTOs.Athletes;

public sealed record MedicalRecordDto(
    Guid? Id,
    Guid AthleteId,
    string? MedicalHistory,
    string? Surgeries,
    string? Pathologies,
    bool HasPhysicalFitnessCertificate,
    DateOnly? PhysicalFitnessIssuedAt,
    DateOnly? PhysicalFitnessExpiresAt,
    string? PhysicalFitnessNotes,
    DateTime? UpdatedAt);
