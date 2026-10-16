using System.Text.Json.Serialization;

namespace Application.DTOs.Ai;

public sealed record RoutineSuggestionRequestDto(
    [property: JsonPropertyName("atletaId")] Guid AthleteId,
    [property: JsonPropertyName("objetivo")] string Goal,
    [property: JsonPropertyName("enfasisGruposMusculares")] List<string>? MuscleGroupEmphasis = null
);

public sealed record SuggestedExerciseDto(
    [property: JsonPropertyName("ejercicioNombre")] string ExerciseName,
    [property: JsonPropertyName("series")] int Sets,
    [property: JsonPropertyName("repeticiones")] int Reps,
    [property: JsonPropertyName("porcentajeSugerido")] decimal SuggestedPercentage,
    [property: JsonPropertyName("ejercicioId")] Guid? ExerciseId = null,
    [property: JsonPropertyName("esReferencia")] bool IsReference = false
);

public sealed record RoutineSuggestionResponseDto(
    [property: JsonPropertyName("atletaId")] Guid AthleteId,
    [property: JsonPropertyName("tituloPropuesto")] string ProposedTitle,
    [property: JsonPropertyName("resumenJustificacion")] string JustificationSummary,
    [property: JsonPropertyName("ejercicios")] List<SuggestedExerciseDto> Exercises,
    [property: JsonPropertyName("proveedorIa")] string ProviderUsed
);
