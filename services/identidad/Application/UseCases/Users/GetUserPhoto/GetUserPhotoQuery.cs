using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Users.GetUserPhoto;

public sealed record GetUserPhotoQuery(Guid UserId);

public sealed class GetUserPhotoQueryHandler(IUserPhotoRepository photoRepository)
{
    public async Task<PhotoDto> HandleAsync(GetUserPhotoQuery query, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByUserIdAsync(query.UserId, cancellationToken);
        if (photo is null)
        {
            throw new PhotoNotFoundException("El usuario no tiene foto de perfil.");
        }

        return new PhotoDto(photo.Data, photo.ContentType);
    }
}

