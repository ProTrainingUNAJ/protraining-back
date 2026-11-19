using Application.DTOs.TrainingPlans;
using Domain.Entities;

namespace Application.UseCases.TrainingPlans;

public static class TrainingPlanMapper
{
    // Ids de todos los ejercicios del plan, para buscar sus nombres en el catálogo.
    public static IEnumerable<Guid> GetExerciseIds(TrainingPlan plan) =>
        plan.Weeks
            .SelectMany(w => w.Sessions)
            .SelectMany(s => s.PrescribedExercises)
            .Select(e => e.ExerciseId)
            .Distinct();

    public static TrainingPlanDetailDto ToDetailDto(TrainingPlan plan, IReadOnlyDictionary<Guid, string> exerciseNames)
    {
        var weekDtos = plan.Weeks
            .OrderBy(w => w.WeekNumber)
            .Select(w => new TrainingPlanWeekDto(
                w.Id,
                w.WeekNumber,
                w.Sessions
                    .OrderBy(s => s.Order)
                    .Select(s =>
                    {
                        var blocksToMap = s.Blocks.Count > 0 ? s.Blocks : GetDefaultLegacyBlocks(s);
                        var blockDtos = blocksToMap
                            .OrderBy(b => b.Order)
                            .Select(b => new TrainingBlockDto(
                                b.Id,
                                b.Title,
                                b.Order,
                                b.PrescribedExercises
                                    .OrderBy(e => e.Order)
                                    .Select(e => MapExerciseDto(e, exerciseNames))
                                    .ToList()
                            )).ToList();

                        var flatExercises = blockDtos.SelectMany(b => b.Exercises).OrderBy(e => e.Order).ToList();

                        return new TrainingPlanSessionDto(
                            s.Id,
                            s.Name,
                            s.Order,
                            s.DayOfWeek,
                            s.CoachNotes,
                            blockDtos,
                            flatExercises,
                            s.StartTime
                        );
                    }).ToList()
            )).ToList();

        return new TrainingPlanDetailDto(
            plan.Id,
            plan.Name,
            plan.Objective,
            weekDtos
        );
    }

    private static List<TrainingBlock> GetDefaultLegacyBlocks(TrainingSession session)
    {
        var defaultBlock = TrainingBlock.Create(session.Id, "Principal", 1);
        foreach (var pe in session.PrescribedExercises)
        {
            defaultBlock.AddPrescribedExercise(pe);
        }
        return [defaultBlock];
    }

    private static PrescribedExerciseDto MapExerciseDto(PrescribedExercise e, IReadOnlyDictionary<Guid, string> exerciseNames)
    {
        return new PrescribedExerciseDto(
            e.Id,
            e.ExerciseId,
            exerciseNames.GetValueOrDefault(e.ExerciseId, string.Empty),
            e.Order,
            e.Sets,
            e.Reps,
            e.TargetPercentage,
            e.Exercise?.ExerciseType ?? ExerciseType.Weight,
            e.Exercise?.Images ?? [],
            e.Exercise?.PrimaryMuscles ?? [],
            e.Exercise?.SecondaryMuscles ?? [],
            e.Exercise?.MuscleGroup,
            e.IntensityType?.ToString(),
            e.TargetRpe,
            e.TargetRir,
            e.Notes
        );
    }
}
