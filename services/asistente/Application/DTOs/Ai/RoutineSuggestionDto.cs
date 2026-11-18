using System.Text.Json.Serialization;
using Application.DTOs.Adaptation;
using Domain.Entities;

namespace Application.DTOs.Ai;

public sealed record PfDecisionContextDto(
    PfDecisionSource Source,
    DateOnly Date,
    string? Zone,
    Guid SuggestedExerciseId,
    PfDecisionType Decision,
    Guid? FinalExerciseId,
    decimal? FinalPercentage
);

public sealed record RoutineSuggestionRequestDto(
    [property: JsonPropertyName("athleteId")] Guid AthleteId,
    [property: JsonPropertyName("goal")] string Goal,
    [property: JsonPropertyName("muscleGroupEmphasis")] List<string>? MuscleGroupEmphasis = null,
    [property: JsonIgnore] IReadOnlyList<CatalogExerciseDto>? AvailableExercises = null,
    [property: JsonIgnore] AthleteAdaptationContextDto? AdaptationContext = null,
    [property: JsonIgnore] IReadOnlyList<PfDecisionContextDto>? PreviousPfDecisions = null,
    [property: JsonPropertyName("baseExercises")] IReadOnlyList<SuggestedExerciseDto>? BaseExercises = null
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
    [property: JsonPropertyName("aiProvider")] string ProviderUsed,
    [property: JsonPropertyName("id")] Guid? Id = null,
    [property: JsonPropertyName("wasAdapted")] bool WasAdapted = false,
    [property: JsonPropertyName("adaptationWarnings")] IReadOnlyList<string>? AdaptationWarnings = null,
    [property: JsonPropertyName("adaptationChanges")] IReadOnlyList<AdaptationChangeDto>? AdaptationChanges = null,
    [property: JsonPropertyName("baseExercises")] IReadOnlyList<SuggestedExerciseDto>? BaseExercises = null
);
