using Application.DTOs.Common;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Organizations.GetOrganizationPhoto;

public sealed record GetOrganizationPhotoQuery(Guid OrganizationId);

public sealed class GetOrganizationPhotoQueryHandler(
    IOrganizationRepository organizationRepository,
    IOrganizationPhotoRepository photoRepository)
{
    public async Task<PhotoDto> HandleAsync(GetOrganizationPhotoQuery query, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(query.OrganizationId, cancellationToken);
        if (organization is null)
        {
            throw new OrganizationNotFoundException(query.OrganizationId);
        }

        var photo = await photoRepository.GetByOrganizationIdAsync(query.OrganizationId, cancellationToken);
        if (photo is null)
        {
            throw new PhotoNotFoundException("La organización no tiene foto o logo cargado.");
        }

        return new PhotoDto(photo.Data, photo.ContentType);
    }
}
