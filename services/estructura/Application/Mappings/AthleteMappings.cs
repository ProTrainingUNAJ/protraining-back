using Application.DTOs.Athletes;
using Domain.Entities;

namespace Application.Mappings;

public static class AthleteMappings
{
    public static AthleteDto ToDto(Athlete athlete) =>
        new(
            athlete.Id,
            athlete.AccountId,
            athlete.FirstName,
            athlete.LastName,
            athlete.FullName,
            athlete.Email,
            athlete.Phone,
            athlete.IsMinor,
            athlete.EmergencyContact,
            athlete.Active,
            athlete.AllowsHabitsRecommendations,
            athlete.DigitalConsent != null
                ? new GuardianConsentDto(
                    athlete.DigitalConsent.IsSigned,
                    athlete.DigitalConsent.ConsentDate,
                    athlete.DigitalConsent.IsMinor,
                    athlete.DigitalConsent.GuardianName,
                    athlete.DigitalConsent.GuardianEmail,
                    athlete.DigitalConsent.GuardianPhone,
                    athlete.DigitalConsent.AllowsPhysicalTraining,
                    athlete.DigitalConsent.AllowsHabitsRecommendations)
                : null);

    public static MedicalRecordDto ToMedicalRecordDto(Guid athleteId, MedicalRecord? record) =>
        record is null
            ? new MedicalRecordDto(
                null,
                athleteId,
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null)
            : new MedicalRecordDto(
                record.Id,
                record.AthleteId,
                record.MedicalHistory,
                record.Surgeries,
                record.Pathologies,
                record.HasPhysicalFitnessCertificate,
                record.PhysicalFitnessIssuedAt,
                record.PhysicalFitnessExpiresAt,
                record.PhysicalFitnessNotes,
                record.UpdatedAt);

    public static MedicalRecordAuditLogDto ToAuditLogDto(MedicalRecordAuditLog auditLog) =>
        new(
            auditLog.Id,
            auditLog.AthleteId,
            auditLog.AccessedByUserId,
            auditLog.Action,
            auditLog.Justification,
            auditLog.CreatedAt);
}
