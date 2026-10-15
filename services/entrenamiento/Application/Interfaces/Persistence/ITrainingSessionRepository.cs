using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ITrainingSessionRepository
{
    Task<TrainingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrainingSession?> GetTodaySessionByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task UpdateAsync(TrainingSession session, CancellationToken cancellationToken = default);
}
