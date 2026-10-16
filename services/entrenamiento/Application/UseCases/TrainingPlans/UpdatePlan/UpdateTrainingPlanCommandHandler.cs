using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.TrainingPlans.UpdatePlan;

public class UpdateTrainingPlanCommandHandler
{
    private readonly ITrainingPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTrainingPlanCommandHandler(ITrainingPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TrainingPlanDetailDto> Handle(UpdateTrainingPlanCommand command, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.GetWithDetailsByIdAsync(command.Id, cancellationToken);
        if (plan == null)
            throw new NotFoundException($"No se encontró la planificación con ID '{command.Id}'.");

        var request = command.Request;

        // Validar existencia de todos los exerciseIds
        var exerciseIds = request.Weeks?
            .SelectMany(w => w.Sessions ?? [])
            .SelectMany(s => s.Exercises ?? [])
            .Select(e => e.ExerciseId)
            .Distinct()
            .ToList() ?? [];

        if (exerciseIds.Count > 0)
        {
            var allExist = await _planRepository.AllExercisesExistAsync(exerciseIds, cancellationToken);
            if (!allExist)
                throw new NotFoundException("Uno o más ejercicios especificados no existen.");
        }

        plan.Update(request.Name, request.Objective);

        // Reemplazo del árbol completo
        plan.ClearWeeks();

        if (request.Weeks != null)
        {
            foreach (var weekReq in request.Weeks)
            {
                var week = TrainingWeek.Create(weekReq.WeekNumber, plan.Id);

                if (weekReq.Sessions != null)
                {
                    foreach (var sessionReq in weekReq.Sessions)
                    {
                        var session = TrainingSession.Create(
                            week.Id,
                            sessionReq.Name,
                            sessionReq.Order,
                            sessionReq.DayOfWeek,
                            sessionReq.CoachNotes
                        );

                        if (sessionReq.Exercises != null)
                        {
                            foreach (var exReq in sessionReq.Exercises)
                            {
                                var exercise = PrescribedExercise.Create(
                                    exReq.Order,
                                    exReq.Sets,
                                    exReq.Reps,
                                    exReq.TargetPercentage,
                                    session.Id,
                                    exReq.ExerciseId
                                );
                                session.AddPrescribedExercise(exercise);
                            }
                        }

                        week.AddSession(session);
                    }
                }

                plan.AddWeek(week);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return TrainingPlanMapper.ToDetailDto(plan);
    }
}
