using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAttendanceRepository
{
    Task<Attendance?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attendance>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default);

    // Correcciones de asistencia de varios atletas en un rango, en una sola consulta (KAN-504).
    Task<IReadOnlyList<Attendance>> GetByAthletesAndDateRangeAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attendance>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default);

    Task AddAsync(Attendance attendance, CancellationToken cancellationToken = default);
}