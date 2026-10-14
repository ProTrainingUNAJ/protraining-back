using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
    Task DeleteAsync(Category category, CancellationToken cancellationToken = default);
    Task<bool> HasAthletesAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
