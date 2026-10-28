using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IDailyLoadRepository
{
    Task<DailyLoad?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);

    Task AddAsync(DailyLoad dailyLoad, CancellationToken cancellationToken = default);
}
