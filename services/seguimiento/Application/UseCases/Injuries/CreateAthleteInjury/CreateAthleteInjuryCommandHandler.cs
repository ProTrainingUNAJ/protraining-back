using Application.DTOs.Injuries;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Injuries.CreateAthleteInjury;

// Registra una lesión nueva: nace en Active con su fecha de retorno estimada (KAN-419).
// Las reglas de negocio viven en AthleteInjury.Create (DomainException → 400 en español).
public sealed class CreateAthleteInjuryCommandHandler(
    IAthleteInjuryRepository repository,
    IUnitOfWork unitOfWork,
    IStructureClient structureClient)
{
    public async Task<AthleteInjuryDto> HandleAsync(
        CreateAthleteInjuryCommand command,
        CancellationToken cancellationToken = default)
    {
        // Atleta de otro PF o inexistente → 404 (KAN-431): no se guarda una lesión de un atleta ajeno.
        if (!await structureClient.IsOwnAthleteAsync(command.AthleteId, cancellationToken))
            throw new AthleteNotFoundException();

        var injury = AthleteInjury.Create(
            command.AthleteId,
            command.Zone,
            command.Description,
            command.InjuryDate,
            command.EstimatedReturnDate);

        await repository.AddAsync(injury, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteInjuryMappings.ToDto(injury);
    }
}
