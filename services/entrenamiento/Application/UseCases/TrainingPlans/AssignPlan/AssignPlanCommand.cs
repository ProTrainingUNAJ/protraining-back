using Application.DTOs.TrainingPlans;

namespace Application.UseCases.TrainingPlans.AssignPlan;

public record AssignPlanCommand(
    Guid TrainingPlanId,
    DateOnly StartDate,
    Guid? CategoryId,
    Guid? AthleteId
);
