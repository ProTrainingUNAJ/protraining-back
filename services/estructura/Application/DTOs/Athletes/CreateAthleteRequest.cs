namespace Application.DTOs.Athletes;

public sealed record CreateAthleteRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool IsMinor,
    string? GuardianName = null,
    string? GuardianEmail = null,
    string? GuardianPhone = null,
    bool AllowsPhysicalTraining = true,
    bool AllowsHabitsRecommendations = false,
    string? EmergencyContact = null,
    string? MedicalConditions = null);
