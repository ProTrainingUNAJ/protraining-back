using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.TrainingPlans.AssignPlan;

public class AssignPlanCommandHandler
{
    private readonly ITrainingPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStructureClient _structureClient;

    public AssignPlanCommandHandler(ITrainingPlanRepository planRepository, IUnitOfWork unitOfWork, IStructureClient structureClient)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
        _structureClient = structureClient;
    }

    public async Task<Guid> Handle(AssignPlanCommand command, CancellationToken cancellationToken = default)
    {
        var exists = await _planRepository.ExistsAsync(command.TrainingPlanId, cancellationToken);
        if (!exists)
            throw new NotFoundException($"No se encontró la planificación con ID '{command.TrainingPlanId}'.");

        var assignment = PlanAssignment.Create(
            command.TrainingPlanId,
            command.StartDate,
            command.CategoryId,
            command.AthleteId
        );

        // Categoría o atleta de otro PF (o inexistentes) → 404, igual que un recurso propio que no existe (KAN-431).
        if (command.CategoryId is Guid categoryId
            && !await _structureClient.IsOwnCategoryAsync(categoryId, cancellationToken))
            throw new NotFoundException("La categoría no existe o no es tuya.");

        if (command.AthleteId is Guid athleteId
            && await _structureClient.GetAthleteByIdAsync(athleteId, cancellationToken) is null)
            throw new NotFoundException("El atleta no existe o no es tuyo.");

        await _planRepository.AddAssignmentAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return assignment.Id;
    }
}
