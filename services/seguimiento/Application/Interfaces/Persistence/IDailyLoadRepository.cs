using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IDailyLoadRepository
{
    Task<DailyLoad?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);

    // Cargas del día de un atleta en un rango de fechas (semana del atleta, KAN-446).
    Task<IReadOnlyList<DailyLoad>> GetByAthleteAndDateRangeAsync(Guid athleteId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Cargas del día de varios atletas en un rango de fechas en lote (tendencias semanales grupales, KAN-489).
    Task<IReadOnlyList<DailyLoad>> GetByAthletesAndDateRangeAsync(IReadOnlyCollection<Guid> athleteIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Cargas del día de varios atletas en una misma fecha, en una sola consulta (asistencia por categoría).
    Task<IReadOnlyList<DailyLoad>> GetByAthletesAndDateAsync(IReadOnlyCollection<Guid> athleteIds, DateTime date, CancellationToken cancellationToken = default);

    // Cargas del día de una fecha para el PF del token (filtro global de PfId): modo degradado de la asistencia.
    Task<IReadOnlyList<DailyLoad>> GetByDateAsync(DateTime date, CancellationToken cancellationToken = default);

    Task AddAsync(DailyLoad dailyLoad, CancellationToken cancellationToken = default);
}
