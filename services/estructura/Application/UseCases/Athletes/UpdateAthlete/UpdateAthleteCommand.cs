using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.UpdateAthlete;

public sealed record UpdateAthleteCommand(
    Guid Id,
    string? Phone,
    string? EmergencyContact,
    string? MedicalConditions);

public sealed class UpdateAthleteCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(
        UpdateAthleteCommand command,
        CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdateContactInfo(command.Phone, command.EmergencyContact);

        // MedicalConditions no tiene método propio en la entidad actual;
        // se expone solo Phone y EmergencyContact. MedicalConditions queda
        // pendiente hasta que el dominio lo exponga explícitamente.

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AthleteMappings.ToDto(athlete);
    }
}
