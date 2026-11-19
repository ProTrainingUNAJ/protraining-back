using Domain.Entities;

namespace Application.DTOs.TrainingPlans;

public record PrescribedExerciseDto(
    Guid Id,
    Guid ExerciseId,
    string ExerciseName,
    int Order,
    int Sets,
    string Reps,
    decimal? TargetPercentage,
    ExerciseType ExerciseType = ExerciseType.Weight,
    List<string>? Images = null,
    List<string>? PrimaryMuscles = null,
    List<string>? SecondaryMuscles = null,
    string? MuscleGroup = null,
    string? IntensityType = null,
    decimal? TargetRpe = null,
    int? TargetRir = null,
    string? Notes = null
);

public record TrainingBlockDto(
    Guid Id,
    string Title,
    int Order,
    List<PrescribedExerciseDto> Exercises
);

public record TrainingPlanSessionDto(
    Guid Id,
    string Name,
    int Order,
    DayOfWeek DayOfWeek,
    string? CoachNotes,
    List<TrainingBlockDto> Blocks,
    List<PrescribedExerciseDto> Exercises,
    TimeOnly? StartTime = null
)
{
    public TrainingPlanSessionDto(
        Guid id,
        string name,
        int order,
        DayOfWeek dayOfWeek,
        string? coachNotes,
        List<PrescribedExerciseDto> exercises,
        TimeOnly? startTime = null)
        : this(id, name, order, dayOfWeek, coachNotes, [], exercises, startTime)
    {
    }
}

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
    string? Objective,
    List<string>? PrimaryMuscles = null,
    List<string>? SecondaryMuscles = null,
    List<string>? TargetMuscles = null
);
