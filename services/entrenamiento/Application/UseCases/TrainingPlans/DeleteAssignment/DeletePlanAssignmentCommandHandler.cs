using Application.Exceptions;
using Application.Interfaces.Persistence;

namespace Application.UseCases.TrainingPlans.DeleteAssignment;

public class DeletePlanAssignmentCommandHandler
{
    private readonly ITrainingPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePlanAssignmentCommandHandler(ITrainingPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(DeletePlanAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        var assignment = await _planRepository.GetAssignmentByIdAsync(command.AssignmentId, cancellationToken);
        if (assignment is null)
            throw new NotFoundException($"No se encontró la asignación con ID '{command.AssignmentId}'.");

        _planRepository.RemoveAssignment(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
