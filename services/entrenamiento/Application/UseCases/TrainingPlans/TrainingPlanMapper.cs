using Application.DTOs.TrainingPlans;
using Domain.Entities;

namespace Application.UseCases.TrainingPlans;

public static class TrainingPlanMapper
{
    public static TrainingPlanDetailDto ToDetailDto(TrainingPlan plan)
    {
        var weekDtos = plan.Weeks
            .OrderBy(w => w.WeekNumber)
            .Select(w => new TrainingPlanWeekDto(
                w.Id,
                w.WeekNumber,
                w.Sessions
                    .OrderBy(s => s.Order)
                    .Select(s => new TrainingPlanSessionDto(
                        s.Id,
                        s.Name,
                        s.Order,
                        s.DayOfWeek,
                        s.CoachNotes,
                        s.PrescribedExercises
                            .OrderBy(e => e.Order)
                            .Select(e => new PrescribedExerciseDto(
                                e.Id,
                                e.ExerciseId,
                                e.Order,
                                e.Sets,
                                e.Reps,
                                e.TargetPercentage
                            )).ToList()
                    )).ToList()
            )).ToList();

        return new TrainingPlanDetailDto(
            plan.Id,
            plan.Name,
            plan.Objective,
            weekDtos
        );
    }
}
