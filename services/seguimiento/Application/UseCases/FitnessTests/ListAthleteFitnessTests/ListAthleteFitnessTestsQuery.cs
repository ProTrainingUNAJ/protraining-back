namespace Application.UseCases.FitnessTests.ListAthleteFitnessTests;

public sealed record ListAthleteFitnessTestsQuery(
    Guid AthleteId,
    Guid? TestId = null,
    DateOnly? From = null,
    DateOnly? To = null);

