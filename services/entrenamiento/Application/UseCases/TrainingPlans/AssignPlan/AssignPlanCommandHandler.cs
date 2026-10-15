using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.TrainingPlans.AssignPlan;

public class AssignPlanCommandHandler
{
    private readonly ITrainingPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AssignPlanCommandHandler(ITrainingPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(AssignPlanCommand command, CancellationToken cancellationToken = default)
    {
        var exists = await _planRepository.ExistsAsync(command.TrainingPlanId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"No se encontró la planificación con ID '{command.TrainingPlanId}'.");

        var assignment = PlanAssignment.Create(
            command.TrainingPlanId,
            command.StartDate,
            command.CategoryId,
            command.AthleteId
        );

        await _planRepository.AddAssignmentAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return assignment.Id;
    }
}
