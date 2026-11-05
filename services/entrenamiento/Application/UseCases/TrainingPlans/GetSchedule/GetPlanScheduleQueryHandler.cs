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
                item.Session.CoachNotes,
                item.Session.StartTime,
                item.Assignment.EndDate))
            // Primero las que tienen hora, en orden; después las de todo el día
            .OrderBy(item => item.StartTime is null)
            .ThenBy(item => item.StartTime)
            .ThenBy(item => item.PlanName)
            .ThenBy(item => item.SessionName)
            .ToList();
    }
}
