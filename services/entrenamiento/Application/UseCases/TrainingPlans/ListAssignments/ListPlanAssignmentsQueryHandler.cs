using Application.DTOs.TrainingPlans;
using Application.Interfaces.Persistence;
using Domain.Common;

namespace Application.UseCases.TrainingPlans.ListAssignments;

public class ListPlanAssignmentsQueryHandler
{
    private readonly ITrainingSessionRepository _sessionRepository;

    public ListPlanAssignmentsQueryHandler(ITrainingSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task<List<PlanAssignmentSummaryDto>> Handle(ListPlanAssignmentsQuery query, CancellationToken cancellationToken = default)
    {
        // Las asignaciones del PF del token (filtro global), con el plan y sus semanas para calcular el fin
        var assignments = await _sessionRepository.ListAssignmentsWithPlansAsync(cancellationToken);
        var today = query.Today ?? ArgentinaTime.Today;

        return assignments
            .Select(assignment => (Assignment: assignment, Status: assignment.StatusOn(today)))
            .Where(item => query.Status is null || item.Status == query.Status)
            // Primero las activas, después las terminadas; dentro de cada grupo, la que empezó más tarde arriba
            .OrderBy(item => item.Status)
            .ThenByDescending(item => item.Assignment.StartDate)
            .Select(item => new PlanAssignmentSummaryDto(
                item.Assignment.Id,
                item.Assignment.TrainingPlanId,
                item.Assignment.Plan.Name,
                item.Assignment.CategoryId,
                item.Assignment.AthleteId,
                item.Assignment.StartDate,
                item.Assignment.EndDate,
                item.Status.ToString()))
            .ToList();
    }
}
