namespace Application.UseCases.Injuries.CreateAthleteInjury;

// Alta de una lesión (KAN-419): el dominio decide el estado inicial (Active) y valida fechas.
public sealed record CreateAthleteInjuryCommand(
    Guid AthleteId,
    string Zone,
    string Description,
    DateTime InjuryDate,
    DateTime? EstimatedReturnDate);
