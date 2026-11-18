using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingPlans.DeletePlan;

public class DeleteTrainingPlanCommandHandler
{
    private readonly ITrainingPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTrainingPlanCommandHandler(ITrainingPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(DeleteTrainingPlanCommand command, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetByIdAsync(command.Id, cancellationToken);
        if (plan is null)
            throw new NotFoundException($"No se encontró la planificación con ID '{command.Id}'.");

        _planRepository.Remove(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
