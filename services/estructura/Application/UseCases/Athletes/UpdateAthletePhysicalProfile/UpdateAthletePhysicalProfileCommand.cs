using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.UpdateAthletePhysicalProfile;

public sealed record UpdateAthletePhysicalProfileCommand(
    Guid Id,
    decimal? WeightKg,
    string? AthleticGoal);

/// <summary>
/// Edición del perfil físico por el preparador físico (KAN-494). El tenant del PF se aplica
/// en el repositorio: un id ajeno devuelve AthleteNotFoundException (404), sin filtrar datos.
/// </summary>
public sealed class UpdateAthletePhysicalProfileCommandHandler(
    IAthleteRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(
        UpdateAthletePhysicalProfileCommand command,
        CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdatePhysicalProfile(command.WeightKg, command.AthleticGoal);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteMappings.ToDto(athlete);
    }
}
