using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IOrganizationPhotoRepository
{
    Task<OrganizationPhoto?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task AddAsync(OrganizationPhoto photo, CancellationToken cancellationToken = default);
    Task DeleteAsync(OrganizationPhoto photo, CancellationToken cancellationToken = default);
}

