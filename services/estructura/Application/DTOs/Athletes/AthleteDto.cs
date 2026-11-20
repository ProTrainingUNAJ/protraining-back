namespace Application.DTOs.Athletes;

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
    bool Active,
    bool AllowsHabitsRecommendations,
    GuardianConsentDto? GuardianConsent,
    bool HasPhoto = false,
    DateTime? PhotoUpdatedAt = null,
    string? TelegramUsername = null,
    decimal? WeightKg = null,
    string? AthleticGoal = null,
    DateTime? PhysicalProfileUpdatedAt = null);
