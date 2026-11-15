using Application.DTOs.Users;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Users.GetPfPhoto;

public sealed record GetPfPhotoQuery(Guid PfId);

public sealed class GetPfPhotoQueryHandler(IUserPhotoRepository photoRepository)
{
    public async Task<PhotoDto> HandleAsync(GetPfPhotoQuery query, CancellationToken cancellationToken = default)
    {
        var photo = await photoRepository.GetByUserIdAsync(query.PfId, cancellationToken);
        if (photo is null)
        {
            throw new PhotoNotFoundException("El preparador físico no tiene foto de perfil cargada.");
        }

        return new PhotoDto(photo.Data, photo.ContentType);
    }
}

