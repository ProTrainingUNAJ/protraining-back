namespace Application.DTOs.TrainingPlans;

public record ApplyPlanTemplateRequest(DateOnly StartDate, Guid? CategoryId = null, Guid? AthleteId = null);
