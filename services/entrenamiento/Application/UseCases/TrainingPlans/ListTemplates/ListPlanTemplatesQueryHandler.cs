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
            return templates.Select(p => new TrainingPlanSummaryDto(p.Id, p.Name, p.Objective)).ToList();
        }
    }
}
