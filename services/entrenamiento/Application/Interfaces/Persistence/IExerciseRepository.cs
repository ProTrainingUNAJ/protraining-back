using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IExerciseRepository
{
    Task<(List<Exercise> Items, int TotalCount)> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, bool usedByPf, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
