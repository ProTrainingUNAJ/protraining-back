using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ITrainingPlanRepository
{
    Task<TrainingPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrainingPlan?> GetWithDetailsByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<TrainingPlan>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> AllExercisesExistAsync(IEnumerable<Guid> exerciseIds, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, string>> GetExerciseNamesAsync(IEnumerable<Guid> exerciseIds, CancellationToken cancellationToken = default);
    Task AddAsync(TrainingPlan plan, CancellationToken cancellationToken = default);
    Task AddAssignmentAsync(PlanAssignment assignment, CancellationToken cancellationToken = default);
    Task <List<TrainingPlan>> ListTemplatesAsync(CancellationToken cancellationToken = default);
}
