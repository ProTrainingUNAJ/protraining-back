using System.Text.Json.Serialization;

namespace Application.DTOs.PlanDrafts;

public sealed record PlanDraftExerciseDto(
    [property: JsonPropertyName("exerciseId")] Guid ExerciseId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("targetPercentage")] decimal? TargetPercentage = null
);

public sealed record PlanDraftSessionDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dayOfWeek"), JsonConverter(typeof(JsonStringEnumConverter))] DayOfWeek DayOfWeek,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("coachNotes")] string? CoachNotes,
    [property: JsonPropertyName("exercises")] List<PlanDraftExerciseDto> Exercises
);

public sealed record PlanDraftWeekDto(
    [property: JsonPropertyName("weekNumber")] int WeekNumber,
    [property: JsonPropertyName("sessions")] List<PlanDraftSessionDto> Sessions
);

public sealed record PlanDraftContentDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("justificationSummary")] string JustificationSummary,
    [property: JsonPropertyName("weeks")] List<PlanDraftWeekDto> Weeks
);

public sealed record CreatePlanDraftRequestDto(
    [property: JsonPropertyName("goal")] string Goal,
    [property: JsonPropertyName("athleteId")] Guid? AthleteId = null,
    [property: JsonPropertyName("categoryId")] Guid? CategoryId = null,
    [property: JsonPropertyName("weeks")] int Weeks = 4,
    [property: JsonPropertyName("daysPerWeek")] int DaysPerWeek = 3,
    [property: JsonPropertyName("trainingDays")] List<string>? TrainingDays = null,
    [property: JsonPropertyName("startDate")] DateOnly? StartDate = null
);

public sealed record UpdatePlanDraftRequestDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("justificationSummary")] string JustificationSummary,
    [property: JsonPropertyName("weeks")] List<PlanDraftWeekDto> Weeks
);

public sealed record PlanDraftResponseDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("categoryId")] Guid? CategoryId,
    [property: JsonPropertyName("athleteId")] Guid? AthleteId,
    [property: JsonPropertyName("goal")] string Goal,
    [property: JsonPropertyName("startDate")] DateOnly StartDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("approvedPlanId")] Guid? ApprovedPlanId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("justificationSummary")] string JustificationSummary,
    [property: JsonPropertyName("weeks")] List<PlanDraftWeekDto> Weeks,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings = null
);

public sealed record SaveTrainingPlanExerciseRequestDto(
    [property: JsonPropertyName("exerciseId")] Guid ExerciseId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("targetPercentage")] decimal? TargetPercentage = null
);

public sealed record SaveTrainingPlanSessionRequestDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("dayOfWeek")] DayOfWeek DayOfWeek,
    [property: JsonPropertyName("coachNotes")] string? CoachNotes = null,
    [property: JsonPropertyName("exercises")] List<SaveTrainingPlanExerciseRequestDto>? Exercises = null
);

public sealed record SaveTrainingPlanWeekRequestDto(
    [property: JsonPropertyName("weekNumber")] int WeekNumber,
    [property: JsonPropertyName("sessions")] List<SaveTrainingPlanSessionRequestDto>? Sessions = null
);

public sealed record SaveTrainingPlanRequestDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string? Objective = null,
    [property: JsonPropertyName("weeks")] List<SaveTrainingPlanWeekRequestDto>? Weeks = null
);

public sealed record AssignTrainingPlanRequestDto(
    [property: JsonPropertyName("trainingPlanId")] Guid TrainingPlanId,
    [property: JsonPropertyName("startDate")] DateOnly StartDate,
    [property: JsonPropertyName("categoryId")] Guid? CategoryId = null,
    [property: JsonPropertyName("athleteId")] Guid? AthleteId = null
);

public sealed record ApprovePlanDraftResponseDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("approvedPlanId")] Guid ApprovedPlanId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("assigned")] bool Assigned,
    [property: JsonPropertyName("assignmentId")] Guid? AssignmentId = null,
    [property: JsonPropertyName("warning")] string? Warning = null,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings = null
);
