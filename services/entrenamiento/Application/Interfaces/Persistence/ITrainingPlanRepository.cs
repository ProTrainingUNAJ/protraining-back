using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ITrainingPlanRepository
{
    Task<TrainingPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAssignmentAsync(PlanAssignment assignment, CancellationToken cancellationToken = default);
}
