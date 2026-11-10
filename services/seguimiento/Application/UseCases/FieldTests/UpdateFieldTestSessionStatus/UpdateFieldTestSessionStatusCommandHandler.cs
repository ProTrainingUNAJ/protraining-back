using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.FieldTests.UpdateFieldTestSessionStatus;

// Transición de estado de la sesión (KAN-427): 1 SELECT (sesión) + 1 UPDATE en un único
// SaveChangesAsync. Sesión ajena → 404. Máquina de estados estricta y secuencial
// (mismo criterio que AthleteInjury.TransitionTo): solo Planned → InProgress → Closed,
// sin saltos ni reaperturas → cualquier otra combinación responde 400.
public sealed class UpdateFieldTestSessionStatusCommandHandler(
    IFieldTestRepository repository,
    IUnitOfWork unitOfWork)
{
    // Estado destino admitido para cada estado actual.
    private static readonly IReadOnlyDictionary<FieldTestSessionStatus, FieldTestSessionStatus> AllowedTransitions =
        new Dictionary<FieldTestSessionStatus, FieldTestSessionStatus>
        {
            [FieldTestSessionStatus.Planned] = FieldTestSessionStatus.InProgress,
            [FieldTestSessionStatus.InProgress] = FieldTestSessionStatus.Closed
        };

    public async Task<FieldTestSessionSummaryDto> HandleAsync(
        UpdateFieldTestSessionStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionByIdForUpdateAsync(command.SessionId, cancellationToken)
            ?? throw new FieldTestSessionNotFoundException();

        if (!AllowedTransitions.TryGetValue(session.Status, out var next) || next != command.Status)
            throw new DomainException(
                $"Transición de estado no válida: {session.Status} → {command.Status}.");

        session.Status = command.Status;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return FieldTestMappings.ToSummaryDto(session);
    }
}
