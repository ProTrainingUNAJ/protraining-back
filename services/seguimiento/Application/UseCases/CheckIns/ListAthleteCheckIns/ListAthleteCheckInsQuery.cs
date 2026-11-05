namespace Application.UseCases.CheckIns.ListAthleteCheckIns;

public sealed record ListAthleteCheckInsQuery(
    Guid AthleteId,
    DateOnly? From = null,
    DateOnly? To = null,
    int? Limit = null);

