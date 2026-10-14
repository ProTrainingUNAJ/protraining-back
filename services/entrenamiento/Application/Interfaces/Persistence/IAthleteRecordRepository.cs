using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteRecordRepository
{
    Task<AthleteRecord?> GetActiveRecordAsync(Guid athleteId, Guid exerciseId, CancellationToken cancellationToken = default);
    Task AddAsync(AthleteRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(AthleteRecord record, CancellationToken cancellationToken = default);
}
