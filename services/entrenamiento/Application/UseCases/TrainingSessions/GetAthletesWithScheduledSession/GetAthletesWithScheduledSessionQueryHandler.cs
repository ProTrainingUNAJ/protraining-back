using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.TrainingSessions.GetAthletesWithScheduledSession;

public class GetAthletesWithScheduledSessionQueryHandler(
    ITrainingSessionRepository sessionRepository,
    IStructureClient structureClient)
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(
        GetAthletesWithScheduledSessionQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Obtener todas las asignaciones con sus planes para el pfId recibido
        var assignments = await sessionRepository.ListAssignmentsWithPlansByPfIdAsync(query.PfId, cancellationToken);

        var athleteIds = new HashSet<Guid>();

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
            // Si está asignado a una categoría entera, consultamos los atletas de esa categoría a Estructura
            else if (assignment.CategoryId.HasValue)
            {
                var categoryAthletes = await structureClient.GetCategoryAthletesAsync(assignment.CategoryId.Value, cancellationToken);
                foreach (var athleteId in categoryAthletes)
                {
                    athleteIds.Add(athleteId);
                }
            }
        }

        return athleteIds.ToList();
    }
}