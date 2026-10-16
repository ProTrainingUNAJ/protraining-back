using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingPlans.GetPlan;

public class GetTrainingPlanQueryHandler
{
    private readonly ITrainingPlanRepository _planRepository;

    public GetTrainingPlanQueryHandler(ITrainingPlanRepository planRepository)
    {
        _planRepository = planRepository;
    }

    public async Task<TrainingPlanDetailDto> Handle(GetTrainingPlanQuery query, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetWithDetailsByIdAsync(query.Id, cancellationToken);
        if (plan == null)
            throw new NotFoundException($"No se encontró la planificación con ID '{query.Id}'.");

        return TrainingPlanMapper.ToDetailDto(plan);
    }
}
