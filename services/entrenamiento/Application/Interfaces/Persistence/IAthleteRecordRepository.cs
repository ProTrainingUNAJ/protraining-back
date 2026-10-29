using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteRecordRepository
{
    Task<AthleteRecord?> GetActiveRecordAsync(Guid athleteId, Guid exerciseId, CancellationToken cancellationToken = default);
    Task<List<AthleteRecord>> ListAsync(Guid athleteId, bool currentOnly, CancellationToken cancellationToken = default);
    Task<string?> GetExerciseNameAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task AddAsync(AthleteRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(AthleteRecord record, CancellationToken cancellationToken = default);
}
