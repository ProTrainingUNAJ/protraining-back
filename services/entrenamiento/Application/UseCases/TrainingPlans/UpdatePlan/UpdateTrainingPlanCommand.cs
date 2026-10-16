using Application.DTOs.TrainingPlans;

namespace Application.UseCases.TrainingPlans.UpdatePlan;

public record UpdateTrainingPlanCommand(Guid Id, SaveTrainingPlanRequest Request);
