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
            athlete.MedicalConditions,
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
}
