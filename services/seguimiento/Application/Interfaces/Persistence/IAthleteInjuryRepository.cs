using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IAthleteInjuryRepository
{
    Task<AthleteInjury?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Lesiones de un atleta, con filtro opcional por estado actual (listado del historial).
    Task<IReadOnlyList<AthleteInjury>> ListByAthleteAsync(Guid athleteId, InjuryStatus? status, CancellationToken cancellationToken = default);

    // Lesiones que siguen activas (Active o InRecovery): las que el atleta debe puntuar en su check-in.
    Task<IReadOnlyList<AthleteInjury>> ListNotClearedByAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);

    // Conteo batch de lesiones en estado Active agrupado por atleta para evitar N+1 (KAN-464).
    Task<IReadOnlyDictionary<Guid, int>> CountActiveByAthleteIdsAsync(
        IReadOnlyCollection<Guid> athleteIds,
        CancellationToken cancellationToken = default);

    Task AddAsync(AthleteInjury injury, CancellationToken cancellationToken = default);

    // Fila del historial clínico: cada cambio de estado deja su registro.
    Task AddStatusChangeAsync(AthleteInjuryStatusChange statusChange, CancellationToken cancellationToken = default);
}
