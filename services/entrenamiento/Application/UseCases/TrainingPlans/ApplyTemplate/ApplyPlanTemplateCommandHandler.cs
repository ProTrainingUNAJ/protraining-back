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

        public async Task<Guid> Handle(ApplyPlanTemplateCommand request, CancellationToken cancellationToken = default)
        {
            var template = await _trainingPlanRepository.GetWithDetailsByIdAsync(request.TemplateId, cancellationToken);
            if (template is null || !template.IsTemplate)
            {
                throw new NotFoundException($"No se encontró la plantilla con ID '{request.TemplateId}'.");
            }

            var newPlan = TrainingPlan.Create(template.Name, template.Objective, false);

            foreach (var week in template.Weeks)
            {
                var newWeek = TrainingWeek.Create(week.WeekNumber, newPlan.Id);
                foreach (var session in week.Sessions)
                {
                    var newSession = TrainingSession.Create (
                        newWeek.Id,
                        session.Name,
                        session.Order,
                        session.DayOfWeek,
                        session.CoachNotes );
                    foreach (var ex in session.PrescribedExercises)
                    {
                        var newEx = PrescribedExercise.Create(
                            ex.Order,
                            ex.Sets,
                            ex.Reps,
                            ex.TargetPercentage,
                            newSession.Id,
                            ex.ExerciseId);
                        newSession.AddPrescribedExercise(newEx);
                    }
                    newWeek.AddSession(newSession);
                }
                newPlan.AddWeek(newWeek);
            }

            await _trainingPlanRepository.AddAsync(newPlan, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var assignCommand = new AssignPlanCommand(
                newPlan.Id,
                request.StartDate,
                request.CategoryId,
                request.AthleteId);

            var assignmentId = await _assignPlanHandler.Handle(assignCommand, cancellationToken);

            return assignmentId;
        }
    }
}
