namespace Application.UseCases.Injuries.GetActiveInjuriesSummary;

public sealed record GetActiveInjuriesSummaryQuery(IReadOnlyCollection<Guid> AthleteIds);
