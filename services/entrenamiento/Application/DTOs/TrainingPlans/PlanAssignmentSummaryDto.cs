namespace Application.DTOs.TrainingPlans;

// Asignación del PF para el listado "mis asignaciones": a una categoría o a un atleta
// (el front resuelve el nombre con Estructura), con su fin calculado y su estado según hoy (KAN-558).
public record PlanAssignmentSummaryDto(
    Guid AssignmentId,
    Guid TrainingPlanId,
    string PlanName,
    Guid? CategoryId,
    Guid? AthleteId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status // "Active" o "Finished"
);
