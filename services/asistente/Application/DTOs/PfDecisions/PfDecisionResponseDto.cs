using System.Text.Json.Serialization;

namespace Application.DTOs.PfDecisions;

public sealed record PfDecisionResponseDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("athleteId")] Guid? AthleteId,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sourceId")] Guid SourceId,
    [property: JsonPropertyName("zone")] string? Zone,
    [property: JsonPropertyName("suggestedExerciseId")] Guid SuggestedExerciseId,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("finalExerciseId")] Guid? FinalExerciseId,
    [property: JsonPropertyName("finalPercentage")] decimal? FinalPercentage,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

