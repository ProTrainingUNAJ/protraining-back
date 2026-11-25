using Application.Common;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.UploadUserPhoto;

public sealed record UploadUserPhotoCommand(Guid UserId, byte[] Data, string ContentType, long FileSize);

public sealed class UploadUserPhotoCommandHandler(
    UserManager<User> userManager,
    IUserPhotoRepository photoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UploadUserPhotoCommand command, CancellationToken cancellationToken = default)
    {
        WebpImageValidator.Validate(command.Data);

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
            ?? throw new DomainException("El usuario no existe.");

        var (width, height) = WebpImageValidator.TryGetDimensions(command.Data);

        var existingPhoto = await photoRepository.GetByUserIdAsync(command.UserId, cancellationToken);
        if (existingPhoto is not null)
        {
            existingPhoto.Update(command.Data, command.ContentType, command.FileSize, width, height);
        }
        else
        {
            var photo = UserPhoto.Create(command.UserId, command.Data, command.ContentType, command.FileSize, width, height);
            await photoRepository.AddAsync(photo, cancellationToken);
        }

        user.HasPhoto = true;
        user.PhotoUpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
