namespace Application.DTOs.CheckIns;

public sealed record CheckInInjuryPainDto(
    Guid InjuryId,
    string Zone,
    int Level);
