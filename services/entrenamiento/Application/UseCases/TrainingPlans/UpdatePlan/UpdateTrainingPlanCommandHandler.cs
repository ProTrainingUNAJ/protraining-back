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

        // Actualización in-place del árbol (conserva IDs de semanas, sesiones y ejercicios)
        var requestedWeeks = request.Weeks ?? [];

        var weeksToRemove = plan.Weeks
            .Where(w => !requestedWeeks.Any(rw => MatchesWeek(w, rw)))
            .ToList();

        foreach (var week in weeksToRemove)
        {
            plan.RemoveWeek(week);
        }

        foreach (var weekReq in requestedWeeks)
        {
            var existingWeek = plan.Weeks.FirstOrDefault(w => MatchesWeek(w, weekReq));

            if (existingWeek == null)
            {
                var newWeek = TrainingWeek.Create(weekReq.WeekNumber, plan.Id);

                if (weekReq.Sessions != null)
                {
                    foreach (var sessionReq in weekReq.Sessions)
                    {
                        var newSession = TrainingSession.Create(
                            newWeek.Id,
                            sessionReq.Name,
                            sessionReq.Order,
                            sessionReq.DayOfWeek,
                            sessionReq.CoachNotes
                        );

                        if (sessionReq.Exercises != null)
                        {
                            foreach (var exReq in sessionReq.Exercises)
                            {
                                var newExercise = PrescribedExercise.Create(
                                    exReq.Order,
                                    exReq.Sets,
                                    exReq.Reps,
                                    exReq.TargetPercentage,
                                    newSession.Id,
                                    exReq.ExerciseId
                                );
                                newSession.AddPrescribedExercise(newExercise);
                            }
                        }

                        newWeek.AddSession(newSession);
                    }
                }

                plan.AddWeek(newWeek);
            }
            else
            {
                var requestedSessions = weekReq.Sessions ?? [];

                var sessionsToRemove = existingWeek.Sessions
                    .Where(s => !requestedSessions.Any(rs => MatchesSession(s, rs)))
                    .ToList();

                foreach (var session in sessionsToRemove)
                {
                    existingWeek.RemoveSession(session);
                }

                foreach (var sessionReq in requestedSessions)
                {
                    var existingSession = existingWeek.Sessions.FirstOrDefault(s => MatchesSession(s, sessionReq));

                    if (existingSession == null)
                    {
                        var newSession = TrainingSession.Create(
                            existingWeek.Id,
                            sessionReq.Name,
                            sessionReq.Order,
                            sessionReq.DayOfWeek,
                            sessionReq.CoachNotes
                        );

                        if (sessionReq.Exercises != null)
                        {
                            foreach (var exReq in sessionReq.Exercises)
                            {
                                var newExercise = PrescribedExercise.Create(
                                    exReq.Order,
                                    exReq.Sets,
                                    exReq.Reps,
                                    exReq.TargetPercentage,
                                    newSession.Id,
                                    exReq.ExerciseId
                                );
                                newSession.AddPrescribedExercise(newExercise);
                            }
                        }

                        existingWeek.AddSession(newSession);
                    }
                    else
                    {
                        existingSession.Update(sessionReq.Name, sessionReq.Order, sessionReq.DayOfWeek, sessionReq.CoachNotes);

                        var requestedExercises = sessionReq.Exercises ?? [];

                        var exercisesToRemove = existingSession.PrescribedExercises
                            .Where(e => !requestedExercises.Any(re => MatchesExercise(e, re)))
                            .ToList();

                        foreach (var exercise in exercisesToRemove)
                        {
                            existingSession.RemovePrescribedExercise(exercise);
                        }

                        foreach (var exReq in requestedExercises)
                        {
                            var existingExercise = existingSession.PrescribedExercises.FirstOrDefault(e => MatchesExercise(e, exReq));

                            if (existingExercise == null)
                            {
                                var newExercise = PrescribedExercise.Create(
                                    exReq.Order,
                                    exReq.Sets,
                                    exReq.Reps,
                                    exReq.TargetPercentage,
                                    existingSession.Id,
                                    exReq.ExerciseId
                                );
                                existingSession.AddPrescribedExercise(newExercise);
                            }
                            else
                            {
                                existingExercise.Update(
                                    exReq.Order,
                                    exReq.Sets,
                                    exReq.Reps,
                                    exReq.TargetPercentage,
                                    exReq.ExerciseId
                                );
                            }
                        }
                    }
                }
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var exerciseNames = await _planRepository.GetExerciseNamesAsync(TrainingPlanMapper.GetExerciseIds(plan), cancellationToken);

        return TrainingPlanMapper.ToDetailDto(plan, exerciseNames);
    }

    private static bool MatchesWeek(TrainingWeek entity, TrainingWeekRequest request)
    {
        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
            return entity.Id == request.Id.Value;

        return entity.WeekNumber == request.WeekNumber;
    }

    private static bool MatchesSession(TrainingSession entity, TrainingSessionRequest request)
    {
        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
            return entity.Id == request.Id.Value;

        return entity.Order == request.Order;
    }

    private static bool MatchesExercise(PrescribedExercise entity, PrescribedExerciseRequest request)
    {
        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
            return entity.Id == request.Id.Value;

        return entity.Order == request.Order;
    }
}
