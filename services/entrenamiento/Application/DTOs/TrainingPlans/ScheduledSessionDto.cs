namespace Application.DTOs.TrainingPlans;

// Sesión que toca en una fecha para una asignación del PF (agenda del calendario).
// La asignación es a una categoría o a un atleta: el front resuelve el nombre con Estructura.
public record ScheduledSessionDto(
    Guid AssignmentId,
    Guid TrainingPlanId,
    string PlanName,
    Guid SessionId,
    string SessionName,
    int WeekNumber,
    Guid? CategoryId,
    Guid? AthleteId,
    int ExerciseCount,
    string? CoachNotes
);
