using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Application.UseCases.Users.DeleteUserPhoto;

public sealed record DeleteUserPhotoCommand(Guid UserId);

public sealed class DeleteUserPhotoCommandHandler(
    UserManager<User> userManager,
    IUserPhotoRepository photoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteUserPhotoCommand command, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByUserIdAsync(command.UserId, cancellationToken);
        if (photo is not null)
        {
            await photoRepository.DeleteAsync(photo, cancellationToken);
        }

        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is not null)
        {
            user.HasPhoto = false;
            user.PhotoUpdatedAt = null;
            await userManager.UpdateAsync(user);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

