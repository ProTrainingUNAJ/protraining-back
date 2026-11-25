using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Athletes.DeleteAthletePhoto;

public sealed record DeleteAthletePhotoCommand(Guid AthleteId);

public sealed class DeleteAthletePhotoCommandHandler(
    IAthleteRepository athleteRepository,
    IAthletePhotoRepository photoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteAthletePhotoCommand command, CancellationToken cancellationToken = default)
    {
        var athlete = await athleteRepository.GetByIdAsync(command.AthleteId, cancellationToken)
            ?? throw new AthleteNotFoundException();

        var photo = await photoRepository.GetByAthleteIdAsync(command.AthleteId, cancellationToken);
        if (photo is not null)
        {
            await photoRepository.DeleteAsync(photo, cancellationToken);
        }

        athlete.RemovePhoto();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

