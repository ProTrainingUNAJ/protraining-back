using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICompetitiveEventRepository
{
    Task<IReadOnlyList<CompetitiveEvent>> ListByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<CompetitiveEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(CompetitiveEvent competitiveEvent, CancellationToken cancellationToken = default);
    Task DeleteAsync(CompetitiveEvent competitiveEvent, CancellationToken cancellationToken = default);
}
