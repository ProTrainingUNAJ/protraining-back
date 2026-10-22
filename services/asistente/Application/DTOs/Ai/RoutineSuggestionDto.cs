using System.Text.Json.Serialization;

namespace Application.DTOs.Ai;

public sealed record RoutineSuggestionRequestDto(
    [property: JsonPropertyName("athleteId")] Guid AthleteId,
    [property: JsonPropertyName("goal")] string Goal,
    [property: JsonPropertyName("muscleGroupEmphasis")] List<string>? MuscleGroupEmphasis = null
);

public sealed record SuggestedExerciseDto(
    [property: JsonPropertyName("exerciseName")] string ExerciseName,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("suggestedPercentage")] decimal SuggestedPercentage,
    [property: JsonPropertyName("exerciseId")] Guid? ExerciseId = null,
    [property: JsonPropertyName("isReference")] bool IsReference = false
);

public sealed record RoutineSuggestionResponseDto(
    [property: JsonPropertyName("athleteId")] Guid AthleteId,
    [property: JsonPropertyName("proposedTitle")] string ProposedTitle,
    [property: JsonPropertyName("justificationSummary")] string JustificationSummary,
    [property: JsonPropertyName("exercises")] List<SuggestedExerciseDto> Exercises,
    [property: JsonPropertyName("aiProvider")] string ProviderUsed
);
