using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.FieldTests.UpdateFieldTestSessionStatus;

// Transición de estado de la sesión: 1 SELECT (sesión) + 1 UPDATE en un único
// SaveChangesAsync. Sesión ajena → 404. Máquina de estados encapsulada en el dominio
// (FieldTestSession.TransitionTo, KAN-568): solo Planned → InProgress → Closed,
// sin saltos ni reaperturas → cualquier otra combinación responde 400.
public sealed class UpdateFieldTestSessionStatusCommandHandler(
    IFieldTestRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<FieldTestSessionSummaryDto> HandleAsync(
        UpdateFieldTestSessionStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionByIdForUpdateAsync(command.SessionId, cancellationToken)
            ?? throw new FieldTestSessionNotFoundException();

        // Validación en el dominio (KAN-568): cualquier violación → DomainException → 400.
        session.TransitionTo(command.Status);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FieldTestMappings.ToSummaryDto(session);
    }
}
