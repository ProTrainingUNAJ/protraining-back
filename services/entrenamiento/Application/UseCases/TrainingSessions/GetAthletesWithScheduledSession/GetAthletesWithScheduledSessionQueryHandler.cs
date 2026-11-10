using Application.DTOs.TrainingSessions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingSessions.GetAthletesWithScheduledSession;

public class GetAthletesWithScheduledSessionQueryHandler(
    ITrainingSessionRepository sessionRepository)
{
    public async Task<ScheduledSessionTargetsResponse> HandleAsync(
        GetAthletesWithScheduledSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Obtener todas las asignaciones con sus planes para el pfId recibido
        var assignments = await sessionRepository.ListAssignmentsWithPlansByPfIdAsync(query.PfId, cancellationToken);

        var athleteIds = new HashSet<Guid>();
        var categoryIds = new HashSet<Guid>();

        foreach (var assignment in assignments)
        {
            // Verifica si la asignación tiene sesión programada para la fecha indicada
            var session = assignment.SessionOn(query.Date);
            if (session == null)
                continue;

            // Si está asignado directamente a un atleta
            if (assignment.AthleteId.HasValue)
            {
                athleteIds.Add(assignment.AthleteId.Value);
            }
            // Si está asignado a una categoría, devolvemos el categoryId para que Estructura resuelva sus atletas
            else if (assignment.CategoryId.HasValue)
            {
                categoryIds.Add(assignment.CategoryId.Value);
            }
        }

        return new ScheduledSessionTargetsResponse(
            athleteIds.ToList(),
            categoryIds.ToList());
    }
}
