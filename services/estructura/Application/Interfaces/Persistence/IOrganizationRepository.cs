using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Organization>> ListAsync(CancellationToken cancellationToken = default);
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Organization organization, CancellationToken cancellationToken = default);
    Task DeleteAsync(Organization organization, CancellationToken cancellationToken = default);
}
