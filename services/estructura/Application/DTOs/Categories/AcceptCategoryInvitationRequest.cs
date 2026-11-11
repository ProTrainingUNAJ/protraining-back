namespace Application.DTOs.Categories;

public sealed record AcceptCategoryInvitationRequest(
    string? FirstName,
    string? LastName,
    string? Phone,
    DateOnly BirthDate,
    string? GuardianName = null,
    string? GuardianEmail = null,
    string? GuardianPhone = null,
    bool AllowsPhysicalTraining = true,
    bool AllowsHabitsRecommendations = false,
    string? EmergencyContact = null
);
