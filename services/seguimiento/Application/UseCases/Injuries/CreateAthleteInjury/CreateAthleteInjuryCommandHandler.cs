using Application.DTOs.Injuries;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Injuries.CreateAthleteInjury;

// Registra una lesión nueva: nace en Active y deja su fila inicial null → Active en el
// historial clínico, en un único SaveChanges. Las reglas de negocio viven en el dominio
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

        // El autor del alta sale del token (claim pf_id).
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
