using Application.DTOs.TrainingPlans;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingPlans.GetSchedule;

public class GetPlanScheduleQueryHandler
{
    private readonly ITrainingSessionRepository _sessionRepository;

    public GetPlanScheduleQueryHandler(ITrainingSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task<List<ScheduledSessionDto>> Handle(GetPlanScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var assignments = await _sessionRepository.ListAssignmentsWithPlansAsync(cancellationToken);

        // Misma regla que la rutina del atleta (PlanAssignment.SessionOn): semana desde el inicio y día de la semana
        return assignments
            .Select(assignment => (Assignment: assignment, Session: assignment.SessionOn(query.Date)))
            .Where(item => item.Session != null)
            .Select(item => new ScheduledSessionDto(
                item.Assignment.Id,
                item.Assignment.TrainingPlanId,
                item.Assignment.Plan.Name,
                item.Session!.Id,
                item.Session.Name,
                item.Assignment.WeekNumberOn(query.Date),
                item.Assignment.CategoryId,
                item.Assignment.AthleteId,
                item.Session.PrescribedExercises.Count,
                item.Session.CoachNotes))
            .OrderBy(item => item.PlanName)
            .ThenBy(item => item.SessionName)
            .ToList();
    }
}
