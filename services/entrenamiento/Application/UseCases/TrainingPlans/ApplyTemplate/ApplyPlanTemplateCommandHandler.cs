using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.TrainingPlans.AssignPlan;
using Domain.Entities;

namespace Application.UseCases.TrainingPlans.ApplyTemplate
{
    public class ApplyPlanTemplateCommandHandler
    {
        private readonly ITrainingPlanRepository _trainingPlanRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly AssignPlanCommandHandler _assignPlanHandler;

        public ApplyPlanTemplateCommandHandler(
            ITrainingPlanRepository trainingPlanRepository,
            IUnitOfWork unitOfWork,
            AssignPlanCommandHandler assignPlanHandler)
        {
            _trainingPlanRepository = trainingPlanRepository;
            _unitOfWork = unitOfWork;
            _assignPlanHandler = assignPlanHandler;
        }

        public async Task<PlanAssignmentDto> Handle(ApplyPlanTemplateCommand request, CancellationToken cancellationToken = default)
        {
            var template = await _trainingPlanRepository.GetWithDetailsByIdAsync(request.TemplateId, cancellationToken);
            if (template is null || !template.IsTemplate)
            {
                throw new NotFoundException($"No se encontró la plantilla con ID '{request.TemplateId}'.");
            }

            var newPlan = template.CloneAs(template.Name, false);

            PlanAssignmentDto? assignment = null;

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _trainingPlanRepository.AddAsync(newPlan, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                var assignCommand = new AssignPlanCommand(
                    newPlan.Id,
                    request.StartDate,
                    request.CategoryId,
                    request.AthleteId);
                assignment = await _assignPlanHandler.Handle(assignCommand, cancellationToken);
            }, cancellationToken);

            return assignment!;
        }
    }
}
