using Application.DTOs.Organizations;
using Domain.Entities;

namespace Application.Mappings;

public static class OrganizationMappings
{
    public static OrganizationDto ToDto(Organization organization) =>
        new(organization.Id, organization.Name, organization.Description);
}
