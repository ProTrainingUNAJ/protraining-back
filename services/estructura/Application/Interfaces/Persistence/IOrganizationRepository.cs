using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Organization>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(Organization Organization, int CategoriesCount, int AthletesCount)>> ListWithCountsAsync(CancellationToken cancellationToken = default);
    Task<(Organization Organization, int CategoriesCount, int AthletesCount)?> GetWithCountsByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Organization organization, CancellationToken cancellationToken = default);
    Task DeleteAsync(Organization organization, CancellationToken cancellationToken = default);
}
