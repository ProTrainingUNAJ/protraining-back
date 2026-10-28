using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface ITrainingSessionRepository
{
    Task<TrainingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    // Sesión que le toca al atleta en una fecha, por asignación directa o por alguna de sus categorías
    Task<TrainingSession?> GetByAthleteIdAsync(Guid athleteId, IReadOnlyList<Guid> categoryIds, DateOnly date, CancellationToken cancellationToken = default);
    // Asignaciones del PF con el plan completo (semanas y sesiones), para la agenda del día
    Task<List<PlanAssignment>> ListAssignmentsWithPlansAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(TrainingSession session, CancellationToken cancellationToken = default);
    // Asignaciones de un PF específico (para el worker desatendido con rol Servicio)
    Task<List<PlanAssignment>> ListAssignmentsWithPlansByPfIdAsync(Guid pfId, CancellationToken cancellationToken = default);
}
