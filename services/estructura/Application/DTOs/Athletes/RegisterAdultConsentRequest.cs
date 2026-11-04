namespace Application.DTOs.Athletes;

public sealed record RegisterAdultConsentRequest(
    bool AllowsHabitsRecommendations = false,
    bool AllowsPhysicalTraining = true);
