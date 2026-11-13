using Application.DTOs.Injuries;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;

namespace Application.UseCases.Injuries.UpdateInjuryStatus;

// Máquina de estados estricta de las lesiones (KAN-420): el dominio valida la transición
// (DomainException → 400 en español) y el handler persiste la lesión modificada junto con
// su fila en el historial clínico, en un único SaveChanges.
public sealed class UpdateInjuryStatusCommandHandler(
    IAthleteInjuryRepository repository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<AthleteInjuryDto> HandleAsync(
        UpdateInjuryStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        // Sin lesión (o si es de otro PF, que el filtro global vuelve invisible) → 404.
        var injury = await repository.GetByIdAsync(command.InjuryId, cancellationToken)
            ?? throw new InjuryNotFoundException();

        // Quién hace el cambio sale del token (claim pf_id), nunca leyendo el JWT a mano.
        var changedBy = currentUser.PfId
            ?? throw new UnauthorizedAccessException("No se puede guardar sin un PF en el token.");

        // Reglas de la máquina de estados: Active → InRecovery → MedicallyCleared.
        var statusChange = injury.TransitionTo(
            command.NewStatus,
            command.Note,
            changedBy,
            DateTime.UtcNow);

        await repository.AddStatusChangeAsync(statusChange, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteInjuryMappings.ToDto(injury);
    }
}
