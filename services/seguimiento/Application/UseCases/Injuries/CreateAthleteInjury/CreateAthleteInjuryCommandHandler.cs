using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Injuries.CreateAthleteInjury;

// Registra una lesión nueva: nace en Active con su fecha de retorno estimada (KAN-419) y
// deja su fila inicial null → Active en el historial clínico (observación de la revisión de
// Enzo), en un único SaveChanges. Las reglas de negocio viven en AthleteInjury.Create
// (DomainException → 400 en español).
public sealed class CreateAthleteInjuryCommandHandler(
    IAthleteInjuryRepository repository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<AthleteInjuryDto> HandleAsync(
        CreateAthleteInjuryCommand command,
        CancellationToken cancellationToken = default)
    {
        var injury = AthleteInjury.Create(
            command.AthleteId,
            command.Zone,
            command.Description,
            command.InjuryDate,
            command.EstimatedReturnDate);

        // Quién registra la lesión sale del token (claim pf_id), igual que en la máquina
        // de estados de KAN-420.
        var changedBy = currentUser.PfId
            ?? throw new UnauthorizedAccessException("No se puede guardar sin un PF en el token.");

        await repository.AddAsync(injury, cancellationToken);
        await repository.AddStatusChangeAsync(
            injury.InitialStatusChange(changedBy, DateTime.UtcNow),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteInjuryMappings.ToDto(injury);
    }
}
