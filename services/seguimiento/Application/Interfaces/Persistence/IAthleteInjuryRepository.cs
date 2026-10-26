using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteInjuryRepository
{
    Task<AthleteInjury?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Lesiones de un atleta, con filtro opcional por estado actual (listado del historial).
    Task<IReadOnlyList<AthleteInjury>> ListByAthleteAsync(Guid athleteId, InjuryStatus? status, CancellationToken cancellationToken = default);

    Task AddAsync(AthleteInjury injury, CancellationToken cancellationToken = default);
}
