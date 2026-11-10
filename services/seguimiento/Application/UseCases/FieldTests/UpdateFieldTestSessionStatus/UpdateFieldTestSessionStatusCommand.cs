using Domain.Entities;

namespace Application.UseCases.FieldTests.UpdateFieldTestSessionStatus;

// Avance de estado de la sesión de test de campo: Planned → InProgress → Closed,
// en orden estricto. Sin saltos (Planned → Closed) ni reaperturas: la secuencia la
// valida el caso de uso → 400.
public sealed record UpdateFieldTestSessionStatusCommand(
    Guid SessionId,
    FieldTestSessionStatus Status);
