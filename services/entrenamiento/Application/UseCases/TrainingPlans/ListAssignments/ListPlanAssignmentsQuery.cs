using Domain.Entities;

namespace Application.UseCases.TrainingPlans.ListAssignments;

// Status opcional: sin filtro devuelve todas; con Active o Finished, solo esas.
public record ListPlanAssignmentsQuery(PlanAssignmentStatus? Status = null, DateOnly? Today = null);
