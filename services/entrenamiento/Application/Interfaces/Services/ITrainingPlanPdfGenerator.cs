using Domain.Entities;

namespace Application.Interfaces.Services;

public interface ITrainingPlanPdfGenerator
{
    byte[] Generate(TrainingPlan plan);
}
