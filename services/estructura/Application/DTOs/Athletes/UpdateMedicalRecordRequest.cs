namespace Application.DTOs.Athletes;

public sealed record UpdateMedicalRecordRequest(
    string? Justification,
    string? MedicalHistory,
    string? Surgeries,
    string? Pathologies,
    bool HasPhysicalFitnessCertificate,
    DateOnly? PhysicalFitnessIssuedAt = null,
    DateOnly? PhysicalFitnessExpiresAt = null,
    string? PhysicalFitnessNotes = null);
