using Application.DTOs.TrainingPlans;

namespace Application.UseCases.TrainingPlans.CreatePlan;

public record CreateTrainingPlanCommand(SaveTrainingPlanRequest Request);
