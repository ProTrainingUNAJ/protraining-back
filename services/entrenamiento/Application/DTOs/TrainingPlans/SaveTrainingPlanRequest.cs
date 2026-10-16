namespace Application.DTOs.TrainingPlans;

public record PrescribedExerciseRequest(
    Guid ExerciseId,
    int Order,
    int Sets,
    int Reps,
    decimal? TargetPercentage = null
);

public record TrainingSessionRequest(
    string Name,
    int Order,
    DayOfWeek DayOfWeek,
    string? CoachNotes = null,
    List<PrescribedExerciseRequest>? Exercises = null
);

public record TrainingWeekRequest(
    int WeekNumber,
    List<TrainingSessionRequest>? Sessions = null
);

public record SaveTrainingPlanRequest(
    string Name,
    string? Objective = null,
    List<TrainingWeekRequest>? Weeks = null
);
