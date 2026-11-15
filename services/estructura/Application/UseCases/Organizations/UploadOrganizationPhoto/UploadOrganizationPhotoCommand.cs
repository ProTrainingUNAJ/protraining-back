using Application.Common;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.Organizations.UploadOrganizationPhoto;

public sealed record UploadOrganizationPhotoCommand(Guid OrganizationId, byte[] Data, string ContentType, long FileSize);

public sealed class UploadOrganizationPhotoCommandHandler(
    IOrganizationRepository organizationRepository,
    IOrganizationPhotoRepository photoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UploadOrganizationPhotoCommand command, CancellationToken cancellationToken = default)
    {
        WebpImageValidator.Validate(command.Data);

        var organization = await organizationRepository.GetByIdAsync(command.OrganizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException();

        var existingPhoto = await photoRepository.GetByOrganizationIdAsync(command.OrganizationId, cancellationToken);
        if (existingPhoto is not null)
        {
            existingPhoto.Update(command.Data, command.ContentType, command.FileSize);
        }
        else
        {
            var photo = OrganizationPhoto.Create(command.OrganizationId, command.Data, command.ContentType, command.FileSize);
            await photoRepository.AddAsync(photo, cancellationToken);
        }

        organization.SetPhotoUpdated(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

