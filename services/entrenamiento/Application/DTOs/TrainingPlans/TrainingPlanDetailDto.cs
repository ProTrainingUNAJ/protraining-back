using Domain.Entities;

namespace Application.DTOs.TrainingPlans;

public record PrescribedExerciseDto(
    Guid Id,
    Guid ExerciseId,
    string ExerciseName,
    int Order,
    int Sets,
    int Reps,
    decimal? TargetPercentage,
    ExerciseType ExerciseType = ExerciseType.Weight
);

public record TrainingPlanSessionDto(
    Guid Id,
    string Name,
    int Order,
    DayOfWeek DayOfWeek,
    string? CoachNotes,
    List<PrescribedExerciseDto> Exercises,
    TimeOnly? StartTime = null
);

public record TrainingPlanWeekDto(
    Guid Id,
    int WeekNumber,
    List<TrainingPlanSessionDto> Sessions
);

public record TrainingPlanDetailDto(
    Guid Id,
    string Name,
    string? Objective,
    List<TrainingPlanWeekDto> Weeks
);

public record TrainingPlanSummaryDto(
    Guid Id,
    string Name,
    string? Objective
);
