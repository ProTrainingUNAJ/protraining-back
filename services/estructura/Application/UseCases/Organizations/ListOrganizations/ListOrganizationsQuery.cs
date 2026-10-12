using Application.DTOs.Organizations;
using Application.Interfaces.Persistence;
using Application.Mappings;

namespace Application.UseCases.Organizations.ListOrganizations;

public sealed record ListOrganizationsQuery;

public sealed class ListOrganizationsQueryHandler(IOrganizationRepository repository)
{
    public async Task<IReadOnlyList<OrganizationDto>> HandleAsync(
        ListOrganizationsQuery query,
        CancellationToken cancellationToken = default) =>
        (await repository.ListAsync(cancellationToken))
            .Select(OrganizationMappings.ToDto)
            .ToList();
}
