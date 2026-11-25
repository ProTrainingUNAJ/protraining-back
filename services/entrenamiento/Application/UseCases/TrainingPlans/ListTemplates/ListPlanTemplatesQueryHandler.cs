using Application.DTOs.TrainingPlans;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingPlans.ListTemplates
{
    public class ListPlanTemplatesQueryHandler
    {
        private readonly ITrainingPlanRepository _trainingPlanRepository;

        public ListPlanTemplatesQueryHandler(ITrainingPlanRepository trainingPlanRepository)
        {
            _trainingPlanRepository = trainingPlanRepository;
        }

        public async Task<List<TrainingPlanSummaryDto>> Handle(ListPlanTemplatesQuery request, CancellationToken cancellationToken = default)
        {
            var templates = await _trainingPlanRepository.ListTemplatesAsync(cancellationToken);
            return templates.Select(p =>
            {
                var primaryMuscles = p.Weeks
                    .SelectMany(w => w.Sessions)
                    .SelectMany(s => s.PrescribedExercises)
                    .Where(pe => pe.Exercise != null)
                    .SelectMany(pe =>
                    {
                        var muscles = new List<string>();
                        if (pe.Exercise.PrimaryMuscles != null && pe.Exercise.PrimaryMuscles.Count > 0)
                            muscles.AddRange(pe.Exercise.PrimaryMuscles);
                        else if (!string.IsNullOrWhiteSpace(pe.Exercise.MuscleGroup))
                            muscles.Add(pe.Exercise.MuscleGroup);

                        return muscles;
                    })
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();

                var secondaryMuscles = p.Weeks
                    .SelectMany(w => w.Sessions)
                    .SelectMany(s => s.PrescribedExercises)
                    .Where(pe => pe.Exercise != null && pe.Exercise.SecondaryMuscles != null)
                    .SelectMany(pe => pe.Exercise.SecondaryMuscles!)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim().ToLowerInvariant())
                    .Where(m => !primaryMuscles.Contains(m))
                    .Distinct()
                    .ToList();

                return new TrainingPlanSummaryDto(
                    p.Id,
                    p.Name,
                    p.Objective,
                    PrimaryMuscles: primaryMuscles,
                    SecondaryMuscles: secondaryMuscles,
                    TargetMuscles: primaryMuscles
                );
            }).ToList();
        }
    }
}
