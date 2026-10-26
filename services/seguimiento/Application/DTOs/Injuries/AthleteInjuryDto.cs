namespace Application.DTOs.Injuries;

// El estado viaja como texto (nombre del enum, p. ej. "MedicallyCleared") para que el
// contrato con el front-end no dependa de la representación numérica del enum.
public sealed record AthleteInjuryDto(
    Guid Id,
    Guid AthleteId,
    string Zone,
    string Description,
    DateTime InjuryDate,
    DateTime EstimatedReturnDate,
    string Status,
    DateTime? ClearedAt,
    DateTime CreatedAt);
