using Application.DTOs.Organizations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Organizations.GetOrganization;

public sealed record GetOrganizationQuery(Guid Id);

public sealed class GetOrganizationQueryHandler(IOrganizationRepository repository)
{
    public async Task<OrganizationDto> HandleAsync(
        GetOrganizationQuery query,
        CancellationToken cancellationToken = default)
    {
        var organization = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (organization is null)
            throw new OrganizationNotFoundException();

        return OrganizationMappings.ToDto(organization);
    }
}
