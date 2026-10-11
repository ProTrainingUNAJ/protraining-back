namespace Application.DTOs;

public sealed record AthleteDto(
    Guid Id,
    Guid? AccountId,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    string Phone,
    bool IsMinor,
    string? EmergencyContact,
    string? MedicalConditions,
    bool Active,
    bool AllowsHabitsRecommendations,
    GuardianConsentDto? GuardianConsent);
