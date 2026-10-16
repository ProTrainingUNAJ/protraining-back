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
        return plans.Select(p => new TrainingPlanSummaryDto(p.Id, p.Name, p.Objective)).ToList();
    }
}
