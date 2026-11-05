namespace Application.UseCases.Evolution.GetAthleteEvolution;

public sealed record GetAthleteEvolutionQuery(
    Guid AthleteId,
    int? Months = null,
    DateOnly? From = null,
    DateOnly? To = null
);

