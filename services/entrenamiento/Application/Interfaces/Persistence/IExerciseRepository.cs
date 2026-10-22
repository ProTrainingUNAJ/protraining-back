using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IExerciseRepository
{
    Task<(List<Exercise> Items, int TotalCount)> ListAsync(string? searchTerm, string? equipment, string? muscleGroup, int page, int pageSize, CancellationToken cancellationToken = default);
}