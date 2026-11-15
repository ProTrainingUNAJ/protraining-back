using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.Organizations.DeleteOrganizationPhoto;

public sealed record DeleteOrganizationPhotoCommand(Guid OrganizationId);

public sealed class DeleteOrganizationPhotoCommandHandler(
    IOrganizationRepository organizationRepository,
    IOrganizationPhotoRepository photoRepository,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(DeleteOrganizationPhotoCommand command, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(command.OrganizationId, cancellationToken)
            ?? throw new OrganizationNotFoundException();

        var photo = await photoRepository.GetByOrganizationIdAsync(command.OrganizationId, cancellationToken);
        if (photo is not null)
        {
            await photoRepository.DeleteAsync(photo, cancellationToken);
        }

        organization.RemovePhoto();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

