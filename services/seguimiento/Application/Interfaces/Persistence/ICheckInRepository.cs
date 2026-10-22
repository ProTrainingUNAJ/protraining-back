using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICheckInRepository
{
    Task<CheckIn?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);

    // Check-ins de varios atletas en una misma fecha, en una sola consulta (semáforo por categoría).
    Task<IReadOnlyList<CheckIn>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default);

    // Check-ins de una fecha para el PF del token (filtro global de PfId): modo degradado del semáforo.
    Task<IReadOnlyList<CheckIn>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default);

    Task AddAsync(CheckIn checkIn, CancellationToken cancellationToken = default);
}