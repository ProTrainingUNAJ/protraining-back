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

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            plan.Update(request.Name, request.Objective);

            // Actualización in-place del árbol (conserva IDs de semanas, sesiones, bloques y ejercicios)
            var requestedWeeks = request.Weeks ?? [];

            // Fase 1: Detectar semanas, sesiones y bloques existentes que cambian de número u orden,
            // y asignarles valores temporales para evitar violaciones de índices únicos (Postgres/SQLite).
            var hasTemporaryChanges = false;
            var tempIndex = -1;

            foreach (var weekReq in requestedWeeks)
            {
                var existingWeek = plan.Weeks.FirstOrDefault(w => MatchesWeek(w, weekReq));
                if (existingWeek == null) continue;

                if (existingWeek.WeekNumber != weekReq.WeekNumber)
                {
                    existingWeek.SetTemporaryWeekNumber(tempIndex--);
                    hasTemporaryChanges = true;
                }

                if (weekReq.Sessions == null) continue;

                foreach (var sessionReq in weekReq.Sessions)
                {
                    var existingSession = existingWeek.Sessions.FirstOrDefault(s => MatchesSession(s, sessionReq));
                    if (existingSession == null) continue;

                    if (existingSession.Order != sessionReq.Order)
                    {
                        existingSession.SetTemporaryOrder(tempIndex--);
                        hasTemporaryChanges = true;
                    }

                    if (sessionReq.Blocks == null) continue;

                    foreach (var blockReq in sessionReq.Blocks)
                    {
                        var existingBlock = existingSession.Blocks.FirstOrDefault(b => MatchesBlock(b, blockReq));
                        if (existingBlock != null && existingBlock.Order != blockReq.Order)
                        {
                            existingBlock.SetTemporaryOrder(tempIndex--);
                            hasTemporaryChanges = true;
                        }
                    }
                }
            }

            if (hasTemporaryChanges)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // Fase 2: Aplicar remociones, actualizaciones finales y agregados
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
                            newWeek.AddSession(CreateSession(newWeek.Id, sessionReq));
                        }
                    }

                    plan.AddWeek(newWeek);
                }
                else
                {
                    existingWeek.Update(weekReq.WeekNumber);

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
                            existingWeek.AddSession(CreateSession(existingWeek.Id, sessionReq));
                        }
                        else
                        {
                            existingSession.Update(sessionReq.Name, sessionReq.Order, sessionReq.DayOfWeek, sessionReq.CoachNotes, sessionReq.StartTime);
                            SyncBlocks(existingSession, sessionReq);
                        }
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        var exerciseNames = await _planRepository.GetExerciseNamesAsync(TrainingPlanMapper.GetExerciseIds(plan), cancellationToken);

        return TrainingPlanMapper.ToDetailDto(plan, exerciseNames);
    }

    private static TrainingSession CreateSession(Guid weekId, TrainingSessionRequest sessionReq)
    {
        var session = TrainingSession.Create(
            weekId,
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

        return session;
    }

    private static void SyncBlocks(TrainingSession session, TrainingSessionRequest sessionReq)
    {
        if (sessionReq.Blocks != null)
        {
            var blocksToRemove = session.Blocks
                .Where(b => !sessionReq.Blocks.Any(rb => MatchesBlock(b, rb)))
                .ToList();

            foreach (var block in blocksToRemove)
            {
                session.RemoveBlock(block);
            }

            foreach (var blockReq in sessionReq.Blocks)
            {
                var existingBlock = session.Blocks.FirstOrDefault(b => MatchesBlock(b, blockReq));

                if (existingBlock == null)
                {
                    var newBlock = TrainingBlock.Create(session.Id, blockReq.Title, blockReq.Order);
                    if (blockReq.Exercises != null)
                    {
                        foreach (var exReq in blockReq.Exercises)
                        {
                            var intensityType = ParseIntensityType(exReq.IntensityType);
                            var newExercise = PrescribedExercise.Create(
                                exReq.Order,
                                exReq.Sets,
                                exReq.Reps,
                                exReq.TargetPercentage,
                                newBlock.Id,
                                exReq.ExerciseId,
                                intensityType,
                                exReq.TargetRpe,
                                exReq.TargetRir,
                                exReq.Notes,
                                session.Id,
                                exReq.TargetDistanceMeters,
                                exReq.TargetTimeSeconds
                            );
                            newBlock.AddPrescribedExercise(newExercise);
                        }
                    }
                    session.AddBlock(newBlock);
                }
                else
                {
                    existingBlock.Update(blockReq.Title, blockReq.Order);
                    SyncBlockExercises(existingBlock, session.Id, blockReq.Exercises);
                }
            }
        }
        else if (sessionReq.Exercises != null)
        {
            var principalBlock = session.Blocks.FirstOrDefault(b => b.Title == "Principal") ?? session.Blocks.FirstOrDefault();
            if (principalBlock == null)
            {
                principalBlock = TrainingBlock.Create(session.Id, "Principal", 1);
                session.AddBlock(principalBlock);
            }
            SyncBlockExercises(principalBlock, session.Id, sessionReq.Exercises);
        }
    }

    private static void SyncBlockExercises(TrainingBlock block, Guid sessionId, List<PrescribedExerciseRequest>? requestedExercises)
    {
        var exercises = requestedExercises ?? [];

        var exercisesToRemove = block.PrescribedExercises
            .Where(e => !exercises.Any(re => MatchesExercise(e, re)))
            .ToList();

        foreach (var exercise in exercisesToRemove)
        {
            block.RemovePrescribedExercise(exercise);
        }

        foreach (var exReq in exercises)
        {
            var existingExercise = block.PrescribedExercises.FirstOrDefault(e => MatchesExercise(e, exReq));
            var intensityType = ParseIntensityType(exReq.IntensityType);

            if (existingExercise == null)
            {
                var newExercise = PrescribedExercise.Create(
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
                    sessionId,
                    exReq.TargetDistanceMeters,
                    exReq.TargetTimeSeconds
                );
                block.AddPrescribedExercise(newExercise);
            }
            else
            {
                existingExercise.Update(
                    exReq.Order,
                    exReq.Sets,
                    exReq.Reps,
                    exReq.TargetPercentage,
                    exReq.ExerciseId,
                    intensityType,
                    exReq.TargetRpe,
                    exReq.TargetRir,
                    exReq.Notes,
                    exReq.TargetDistanceMeters,
                    exReq.TargetTimeSeconds
                );
            }
        }
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

    private static bool MatchesBlock(TrainingBlock entity, TrainingBlockRequest request)
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
