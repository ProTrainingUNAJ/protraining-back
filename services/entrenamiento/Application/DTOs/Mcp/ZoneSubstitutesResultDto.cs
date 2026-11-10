using Application.DTOs.Exercise;

namespace Application.DTOs.Mcp;

// Resultado compacto de la herramienta MCP ObtenerSustitutos (E51).
public sealed record ZoneSubstitutesResultDto(
    string Zone,
    IReadOnlyList<ExerciseSubstituteDto> Substitutes);
