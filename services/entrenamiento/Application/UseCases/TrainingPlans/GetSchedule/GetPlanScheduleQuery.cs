namespace Application.UseCases.TrainingPlans.GetSchedule;

// Agenda del PF: las sesiones que tocan en una fecha (hora Argentina) en todas sus asignaciones
public record GetPlanScheduleQuery(DateOnly Date);
