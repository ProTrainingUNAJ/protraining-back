namespace Application.UseCases.TrainingPlans.ApplyTemplate;

public record ApplyPlanTemplateCommand(Guid TemplateId, DateOnly StartDate, Guid? CategoryId = null, Guid? AthleteId = null);
