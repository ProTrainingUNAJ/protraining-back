using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICheckInRepository
{
    Task<CheckIn?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);
    Task AddAsync(CheckIn checkIn, CancellationToken cancellationToken = default);
}