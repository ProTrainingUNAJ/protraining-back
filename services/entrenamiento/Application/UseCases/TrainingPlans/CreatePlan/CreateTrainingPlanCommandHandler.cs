using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.TrainingPlans.CreatePlan;

public class CreateTrainingPlanCommandHandler
{
    private readonly ITrainingPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateTrainingPlanCommandHandler(ITrainingPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TrainingPlanDetailDto> Handle(CreateTrainingPlanCommand command, CancellationToken cancellationToken = default)
    {
        var request = command.Request;

        // Validar existencia de todos los exerciseIds
        var exerciseIds = request.Weeks?
            .SelectMany(w => w.Sessions ?? [])
            .SelectMany(s => (s.Blocks != null && s.Blocks.Count > 0
                ? s.Blocks.SelectMany(b => b.Exercises ?? [])
                : s.Exercises ?? []))
            .Select(e => e.ExerciseId)
            .Distinct()
            .ToList() ?? [];

        if (exerciseIds.Count > 0)
        {
            var allExist = await _planRepository.AllExercisesExistAsync(exerciseIds, cancellationToken);
            if (!allExist)
                throw new NotFoundException("Uno o más ejercicios especificados no existen.");
        }

        var plan = TrainingPlan.Create(request.Name, request.Objective);

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
                            sessionReq.CoachNotes,
                            sessionReq.StartTime
                        );

                        if (sessionReq.Blocks != null && sessionReq.Blocks.Count > 0)
                        {
                            foreach (var blockReq in sessionReq.Blocks)
                            {
                                var block = TrainingBlock.Create(session.Id, blockReq.Title, blockReq.Order);
                                if (blockReq.Exercises != null)
                                {
                                    foreach (var exReq in blockReq.Exercises)
                                    {
                                        var intensityType = ParseIntensityType(exReq.IntensityType);
                                        var exercise = PrescribedExercise.Create(
                                            exReq.Order,
                                            exReq.Sets,
                                            exReq.Reps,
                                            exReq.TargetPercentage,
                                            block.Id,
                                            exReq.ExerciseId,
                                            intensityType,
                                            exReq.TargetRpe,
                                            exReq.TargetRir,
                                            exReq.Notes,
                                            session.Id,
                                            exReq.TargetDistanceMeters,
                                            exReq.TargetTimeSeconds
                                        );
                                        block.AddPrescribedExercise(exercise);
                                    }
                                }
                                session.AddBlock(block);
                            }
                        }
                        else if (sessionReq.Exercises != null && sessionReq.Exercises.Count > 0)
                        {
                            var defaultBlock = TrainingBlock.Create(session.Id, "Principal", 1);
                            foreach (var exReq in sessionReq.Exercises)
                            {
                                var intensityType = ParseIntensityType(exReq.IntensityType);
                                var exercise = PrescribedExercise.Create(
                                    exReq.Order,
                                    exReq.Sets,
                                    exReq.Reps,
                                    exReq.TargetPercentage,
                                    defaultBlock.Id,
                                    exReq.ExerciseId,
                                    intensityType,
                                    exReq.TargetRpe,
                                    exReq.TargetRir,
                                    exReq.Notes,
                                    session.Id,
                                    exReq.TargetDistanceMeters,
                                    exReq.TargetTimeSeconds
                                );
                                defaultBlock.AddPrescribedExercise(exercise);
                            }
                            session.AddBlock(defaultBlock);
                        }

                        week.AddSession(session);
                    }
                }

                plan.AddWeek(week);
            }
        }

        await _planRepository.AddAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var exerciseNames = await _planRepository.GetExerciseNamesAsync(TrainingPlanMapper.GetExerciseIds(plan), cancellationToken);

        return TrainingPlanMapper.ToDetailDto(plan, exerciseNames);
    }

    private static IntensityType? ParseIntensityType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToUpperInvariant();
        if (Enum.TryParse<IntensityType>(normalized, true, out var result) && result != IntensityType.None)
        {
            return result;
        }

        throw new DomainException("La métrica de intensidad debe ser 'RPE' o 'RIR'.");
    }
}
