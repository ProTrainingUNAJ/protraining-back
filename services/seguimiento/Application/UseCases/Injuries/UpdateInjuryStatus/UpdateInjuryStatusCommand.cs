using Domain.Entities;

namespace Application.UseCases.Injuries.UpdateInjuryStatus;

// Transición de estado de una lesión (KAN-420): el dominio decide si es válida.
public sealed record UpdateInjuryStatusCommand(
    Guid InjuryId,
    InjuryStatus NewStatus,
    string? Note);
