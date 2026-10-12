using Application.DTOs.Organizations;
using Application.Interfaces.Persistence;
using Application.Mappings;
using Domain.Entities;

namespace Application.UseCases.Organizations.CreateOrganization;

public sealed record CreateOrganizationCommand(string? Name, string? Description);

public sealed class CreateOrganizationCommandHandler(
    IOrganizationRepository repository,
    IUnitOfWork unitOfWork)
{
    public async Task<OrganizationDto> HandleAsync(
        CreateOrganizationCommand command,
        CancellationToken cancellationToken = default)
    {
        var organization = Organization.Create(command.Name, command.Description);
        await repository.AddAsync(organization, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OrganizationMappings.ToDto(organization);
    }
}
