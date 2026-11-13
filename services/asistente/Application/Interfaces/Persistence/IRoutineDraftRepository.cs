namespace Application.Interfaces.Persistence;

using Domain.Entities;

/// <summary>
/// Contrato de persistencia para los borradores de rutinas generados con asistencia de IA (#088 · KAN-383).
/// </summary>
public interface IRoutineDraftRepository
{
    Task AddAsync(RoutineDraft routineDraft, CancellationToken cancellationToken = default);
    Task<RoutineDraft?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoutineDraft>> GetByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default);
}
