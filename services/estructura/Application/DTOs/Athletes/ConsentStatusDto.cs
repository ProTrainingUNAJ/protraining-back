namespace Application.DTOs.Athletes;

public sealed record ConsentStatusDto(
    bool HasSignedConsent,
    bool IsMinor,
    DateTime? ConsentDate,
    bool AllowsPhysicalTraining,
    bool AllowsHabitsRecommendations,
    string? GuardianName);
