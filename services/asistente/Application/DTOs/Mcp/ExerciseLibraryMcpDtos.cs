using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.DTOs.Mcp;

/// <summary>
/// Tipo de medición del ejercicio en la biblioteca compartida de Entrenamiento (E51 · KAN-484).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum McpExerciseType
{
    Weight,
    Time,
    Distance,
    Reps
}

/// <summary>
/// Ejercicio compacto devuelto por la herramienta MCP BuscarEjercicios (E51 · KAN-484).
/// </summary>
public sealed record McpExerciseCatalogItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MuscleGroup { get; init; } = string.Empty;
    public string? Equipment { get; init; }
    public IReadOnlyList<string> PrimaryMuscles { get; init; } = [];
    public IReadOnlyList<string> SecondaryMuscles { get; init; } = [];
    public McpExerciseType ExerciseType { get; init; } = McpExerciseType.Weight;
    public Guid? ParentExerciseId { get; init; }
    public string? VariantLabel { get; init; }
}

/// <summary>
/// Resultado de la herramienta MCP BuscarEjercicios (E51 · KAN-484).
/// </summary>
public sealed record McpExerciseSearchResultDto(
    int Total,
    IReadOnlyList<McpExerciseCatalogItemDto> Items);

/// <summary>
/// Ejercicio sustituto autorizado devuelto por las herramientas MCP de Entrenamiento (E51 · KAN-484).
/// </summary>
public sealed record McpExerciseSubstituteDto(
    Guid ExerciseId,
    string Name);

/// <summary>
/// Resultado de la herramienta MCP ObtenerSustitutos para una zona de dolor o molestia (E51 · KAN-484).
/// </summary>
public sealed record McpZoneSubstitutesResultDto(
    string Zone,
    IReadOnlyList<McpExerciseSubstituteDto> Substitutes);

/// <summary>
/// Ejercicio contraindicado con sus sustitutos devuelto por la herramienta MCP ObtenerRestricciones (E51 · KAN-484).
/// </summary>
public sealed record McpRestrictedExerciseItemDto(
    Guid ExerciseId,
    string ExerciseName,
    string Zone,
    int MinPainLevel,
    string Reason,
    IReadOnlyList<McpExerciseSubstituteDto> Substitutes);

/// <summary>
/// Resultado de la herramienta MCP ObtenerRestricciones para una zona de dolor o molestia (E51 · KAN-484).
/// </summary>
public sealed record McpZoneRestrictionsResultDto(
    string Zone,
    IReadOnlyList<McpRestrictedExerciseItemDto> Restrictions);

/// <summary>
/// Definición de herramienta expuesta por el servidor MCP de Entrenamiento,
/// lista para inyectarse en llamadas de tool calling del agente de IA (E51 · KAN-484 / KAN-600).
/// </summary>
public sealed record McpToolDefinitionDto(
    string Name,
    string? Description,
    JsonElement InputSchema);
