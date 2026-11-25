using Application.DTOs.Exercise;

namespace Application.DTOs.Mcp;

public sealed record RestrictedExerciseItemDto(
    Guid ExerciseId,
    string ExerciseName,
    string Zone,
    int MinPainLevel,
    string Reason,
    IReadOnlyList<ExerciseSubstituteDto> Substitutes);

// Resultado compacto de la herramienta MCP ObtenerRestricciones (E51).
public sealed record ZoneRestrictionsResultDto(
    string Zone,
    IReadOnlyList<RestrictedExerciseItemDto> Restrictions);
