using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.UpdateAthletePhysicalProfile;

public sealed record UpdateAthletePhysicalProfileCommand(
    Guid Id,
    decimal? WeightKg,
    string? AthleticGoal,
    DateOnly? BirthDate = null,
    string? Gender = null,
    decimal? HeightCm = null);

/// <summary>
/// Edición del perfil físico por el preparador físico. El tenant del PF se aplica
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

        athlete.UpdatePhysicalProfile(command.WeightKg, command.HeightCm, command.AthleticGoal);
        if (command.BirthDate.HasValue || command.Gender is not null)
        {
            athlete.UpdatePersonalInfo(command.BirthDate, command.Gender);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteMappings.ToDto(athlete);
    }
}
