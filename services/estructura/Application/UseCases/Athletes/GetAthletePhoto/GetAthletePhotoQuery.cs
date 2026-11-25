using Application.DTOs.Common;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Athletes.GetAthletePhoto;

public sealed record GetAthletePhotoQuery(Guid AthleteId);

public sealed class GetAthletePhotoQueryHandler(
    IAthleteRepository athleteRepository,
    IAthletePhotoRepository photoRepository)
{
    public async Task<PhotoDto> HandleAsync(GetAthletePhotoQuery query, CancellationToken cancellationToken = default)
    {
        var athlete = await athleteRepository.GetByIdAsync(query.AthleteId, cancellationToken);
        if (athlete is null)
        {
            throw new AthleteNotFoundException(query.AthleteId);
        }

        if (!athlete.AllowsProfilePhoto)
        {
            throw new PhotoNotFoundException("El atleta menor de edad no tiene autorización del tutor legal para mostrar foto de perfil.");
        }

        var photo = await photoRepository.GetByAthleteIdAsync(query.AthleteId, cancellationToken);
        if (photo is null)
        {
            throw new PhotoNotFoundException("El atleta no tiene foto de perfil.");
        }

        return new PhotoDto(photo.Data, photo.ContentType);
    }
}
