using Application.DTOs.Organizations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Organizations.UpdateOrganization;

public sealed record UpdateOrganizationCommand(Guid Id, string? Name, string? Description);

public sealed class UpdateOrganizationCommandHandler(
    IOrganizationRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<OrganizationDto> HandleAsync(
        UpdateOrganizationCommand command,
        CancellationToken cancellationToken = default)
    {
        var organization = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (organization is null)
            throw new OrganizationNotFoundException();

        organization.Rename(command.Name);
        organization.UpdateDescription(command.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OrganizationMappings.ToDto(organization);
    }
}
