namespace Application.DTOs;

public sealed record GuardianConsentDto(
    bool IsSigned,
    DateTime ConsentDate,
    bool IsMinor,
    string? GuardianName,
    string? GuardianEmail,
    string? GuardianPhone,
    bool AllowsPhysicalTraining,
    bool AllowsHabitsRecommendations);
