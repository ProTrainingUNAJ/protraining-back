using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.UpdateAthletePhone;

public sealed record UpdateAthletePhoneCommand(Guid Id, string Phone);

public sealed class UpdateAthletePhoneCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(
        UpdateAthletePhoneCommand command,
        CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdateContactInfo(command.Phone, athlete.EmergencyContact);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AthleteMappings.ToDto(athlete);
    }
}
