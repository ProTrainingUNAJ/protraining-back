using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ICheckInRepository
{
    // Check-in del atleta para una fecha (upsert del reporte diario).
    // La fecha es un date-only: se compara tal cual, sin horas.
    Task<CheckIn?> GetByAthleteAndDateAsync(Guid athleteId, DateTime date, CancellationToken cancellationToken = default);

    Task AddAsync(CheckIn checkIn, CancellationToken cancellationToken = default);
}
