namespace Application.Common.PlanDrafts;

using System.Text.Json.Serialization;

/// <summary>
/// Tipos de bloques funcionales que componen una sesión deportiva estructurada.
/// </summary>
public enum SessionBlockType
{
    WarmUp,
    StrengthA,
    StrengthB,
    Accessories
}

/// <summary>
/// Representa un ejercicio dentro de un bloque funcional seleccionado por índice numérico (1..N).
/// </summary>
public sealed record BlockExerciseItemDto(
    [property: JsonPropertyName("exerciseIndex")] int ExerciseIndex,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("targetPercentage")] decimal? TargetPercentage = null
);

/// <summary>
/// Representa la estructura de una sesión modelo dividida en bloques funcionales.
/// </summary>
public sealed record BlockSessionModelDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dayOfWeek")] string DayOfWeek,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("coachNotes")] string? CoachNotes,
    [property: JsonPropertyName("warmUp")] List<BlockExerciseItemDto> WarmUp,
    [property: JsonPropertyName("strengthA")] List<BlockExerciseItemDto> StrengthA,
    [property: JsonPropertyName("strengthB")] List<BlockExerciseItemDto> StrengthB,
    [property: JsonPropertyName("accessories")] List<BlockExerciseItemDto> Accessories
);

/// <summary>
/// Representa el borrador de una semana modelo generado por la IA estructurado en bloques.
/// </summary>
public sealed record BlockWeekModelDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("objective")] string Objective,
    [property: JsonPropertyName("justificationSummary")] string JustificationSummary,
    [property: JsonPropertyName("modelSessions")] List<BlockSessionModelDto> ModelSessions
);
