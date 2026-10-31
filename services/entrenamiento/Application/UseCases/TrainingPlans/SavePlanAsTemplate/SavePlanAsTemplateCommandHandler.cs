using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;

namespace Application.UseCases.TrainingPlans.SavePlanAsTemplate
{
    public class SavePlanAsTemplateCommandHandler
    {
        private readonly ITrainingPlanRepository _trainingPlanRepository;
        private readonly IUnitOfWork _unitOfWork;

        public SavePlanAsTemplateCommandHandler(ITrainingPlanRepository trainingPlanRepository, IUnitOfWork unitOfWork)
        {
            _trainingPlanRepository = trainingPlanRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<TrainingPlanDetailDto> Handle(SavePlanAsTemplateCommand request, CancellationToken cancellationToken = default)
        {
            var exist = await _trainingPlanRepository.ExistsAsync(request.PlanId, cancellationToken);

            if (!exist)
            {
                throw new NotFoundException($"No se encontró la planificación con ID '{request.PlanId}'.");
            }
            
            var template = await _trainingPlanRepository.GetWithDetailsByIdAsync(request.PlanId, cancellationToken);

            var planTemplate = TrainingPlan.Create(request.Name, template.Objective, true);

            foreach (var week in template.Weeks)
            {
                var newWeek = TrainingWeek.Create(week.WeekNumber, planTemplate.Id);
                foreach (var session in week.Sessions)
                {
                    var newSession = TrainingSession.Create(newWeek.Id, session.Name, session.Order, session.DayOfWeek, session.CoachNotes);
                    foreach (var ex in session.PrescribedExercises)
                    {
                        var newEx = PrescribedExercise.Create(ex.Order, ex.Sets, ex.Reps, ex.TargetPercentage, newSession.Id, ex.ExerciseId);
                        newSession.AddPrescribedExercise(newEx);
                    }
                    newWeek.AddSession(newSession);
                }
                planTemplate.AddWeek(newWeek);
            }

            await _trainingPlanRepository.AddAsync(planTemplate, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var exerciseNames = await _trainingPlanRepository.GetExerciseNamesAsync(TrainingPlanMapper.GetExerciseIds(planTemplate), cancellationToken);

            return TrainingPlanMapper.ToDetailDto(planTemplate, exerciseNames);
        }
    }
}
