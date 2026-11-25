using Application.Common;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Athletes.UploadAthletePhoto;

public sealed record UploadAthletePhotoCommand(Guid AthleteId, byte[] Data, string ContentType, long FileSize);

public sealed class UploadAthletePhotoCommandHandler(
    IAthleteRepository athleteRepository,
    IAthletePhotoRepository photoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UploadAthletePhotoCommand command, CancellationToken cancellationToken = default)
    {
        WebpImageValidator.Validate(command.Data);

        var athlete = await athleteRepository.GetByIdAsync(command.AthleteId, cancellationToken)
            ?? throw new AthleteNotFoundException(command.AthleteId);

        if (!athlete.AllowsProfilePhoto)
        {
            throw new DomainException("El atleta menor de edad no tiene autorización del tutor legal para tener foto de perfil.");
        }

        var (width, height) = WebpImageValidator.TryGetDimensions(command.Data);

        var existingPhoto = await photoRepository.GetByAthleteIdAsync(command.AthleteId, cancellationToken);
        if (existingPhoto is not null)
        {
            existingPhoto.Update(command.Data, command.ContentType, command.FileSize, width, height);
        }
        else
        {
            var photo = AthletePhoto.Create(command.AthleteId, command.Data, command.ContentType, command.FileSize, width, height);
            await photoRepository.AddAsync(photo, cancellationToken);
        }

        athlete.SetPhotoUpdated(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
