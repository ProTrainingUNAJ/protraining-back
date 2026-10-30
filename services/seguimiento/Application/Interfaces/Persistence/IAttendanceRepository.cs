using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAttendanceRepository
{
    Task<Attendance?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attendance>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attendance>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default);

    Task AddAsync(Attendance attendance, CancellationToken cancellationToken = default);
}