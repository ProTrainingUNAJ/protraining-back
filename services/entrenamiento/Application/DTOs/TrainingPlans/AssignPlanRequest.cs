namespace Application.DTOs.TrainingPlans;

public record AssignPlanRequest(
    Guid TrainingPlanId,
    DateOnly StartDate,
    Guid? CategoryId = null,
    Guid? AthleteId = null
);
