using Application.DTOs.Athletes;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Athletes.UpdateAthleteEmail;

public sealed record UpdateAthleteEmailCommand(
    Guid Id,
    string Email);

public sealed class UpdateAthleteEmailCommandHandler(IAthleteRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<AthleteDto> HandleAsync(
        UpdateAthleteEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        var athlete = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (athlete is null)
            throw new AthleteNotFoundException();

        athlete.UpdateEmail(command.Email);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AthleteMappings.ToDto(athlete);
    }
}
