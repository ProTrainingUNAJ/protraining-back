using Application.DTOs.Organizations;
using Domain.Entities;

namespace Application.Mappings;

public static class OrganizationMappings
{
    public static OrganizationDto ToDto(Organization organization, int categoriesCount = 0, int athletesCount = 0) =>
        new(organization.Id, organization.Name, organization.Description, categoriesCount, athletesCount, organization.HasPhoto, organization.PhotoUpdatedAt);
}
