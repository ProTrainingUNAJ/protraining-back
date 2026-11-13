using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ISeasonPhaseRepository
{
    Task<IReadOnlyList<SeasonPhase>> ListByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task<SeasonPhase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(SeasonPhase phase, CancellationToken cancellationToken = default);
    Task DeleteAsync(SeasonPhase phase, CancellationToken cancellationToken = default);
    Task<bool> HasOverlappingPhaseAsync(Guid categoryId, DateOnly startDate, DateOnly endDate, Guid? excludePhaseId = null, CancellationToken cancellationToken = default);
}
