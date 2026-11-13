using Domain.Entities;

namespace Application.DTOs.TrainingPlans;

// Asignación creada, con su fecha de fin calculada (inicio + semanas × 7 − 1) y su estado según hoy (KAN-558).
public record PlanAssignmentDto(
    Guid AssignmentId,
    Guid TrainingPlanId,
    DateOnly StartDate,
    DateOnly EndDate,
    PlanAssignmentStatus Status
);
