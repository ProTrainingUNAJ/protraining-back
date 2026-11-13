using Domain.Entities;

namespace Application.Interfaces.Persistence
{
    public interface IExerciseVideoRepository
    {
        Task AddAsync(ExerciseVideo video ,CancellationToken cancellationToken = default);
        void Remove(ExerciseVideo video);
        Task<ExerciseVideo?> GetByExerciseIdAsync(Guid exerciseId, CancellationToken cancellationToken = default);
        Task<Dictionary<Guid, string>> GetVideosByExerciseIdsAsync(IEnumerable<Guid> exerciseIds, CancellationToken cancellationToken = default);
    }
}
