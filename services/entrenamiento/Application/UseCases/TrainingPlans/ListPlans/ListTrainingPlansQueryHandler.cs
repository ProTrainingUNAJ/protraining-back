using Application.DTOs.TrainingPlans;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingPlans.ListPlans;

public class ListTrainingPlansQueryHandler
{
    private readonly ITrainingPlanRepository _planRepository;

    public ListTrainingPlansQueryHandler(ITrainingPlanRepository planRepository)
    {
        _planRepository = planRepository;
    }

    public async Task<List<TrainingPlanSummaryDto>> Handle(ListTrainingPlansQuery query, CancellationToken cancellationToken = default)
    {
        var plans = await _planRepository.ListAsync(cancellationToken);
        return plans.Select(p =>
        {
            var targetMuscles = p.Weeks
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

                    if (pe.Exercise.SecondaryMuscles != null && pe.Exercise.SecondaryMuscles.Count > 0)
                        muscles.AddRange(pe.Exercise.SecondaryMuscles);

                    return muscles;
                })
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            return new TrainingPlanSummaryDto(p.Id, p.Name, p.Objective, targetMuscles);
        }).ToList();
    }
}
