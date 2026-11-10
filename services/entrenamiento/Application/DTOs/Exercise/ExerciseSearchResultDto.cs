using Domain.Entities;

namespace Application.DTOs.Exercise;

// E51: resultado compacto para el servidor MCP. No incluye descripciones, imágenes ni videos
// para no inflar el contexto de las herramientas de IA que consumen la biblioteca.
public sealed record ExerciseCatalogItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MuscleGroup { get; init; } = string.Empty;
    public string? Equipment { get; init; }
    public IReadOnlyList<string> PrimaryMuscles { get; init; } = [];
    public IReadOnlyList<string> SecondaryMuscles { get; init; } = [];
    public ExerciseType ExerciseType { get; init; }
    public Guid? ParentExerciseId { get; init; }
    public string? VariantLabel { get; init; }
}

public sealed record ExerciseSearchResultDto(
    int Total,
    IReadOnlyList<ExerciseCatalogItemDto> Items);
