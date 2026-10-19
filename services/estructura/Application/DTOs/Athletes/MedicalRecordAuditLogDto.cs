namespace Application.DTOs.Athletes;

public sealed record MedicalRecordAuditLogDto(
    Guid Id,
    Guid AthleteId,
    Guid AccessedByUserId,
    string Action,
    string Justification,
    DateTime CreatedAt);
