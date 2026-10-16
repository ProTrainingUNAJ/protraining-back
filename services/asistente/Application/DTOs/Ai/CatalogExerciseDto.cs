namespace Application.DTOs.Ai;

/// <summary>
/// Representa un ejercicio registrado en el catálogo oficial de la biblioteca deportiva.
/// </summary>
public sealed record CatalogExerciseDto(
    Guid Id,
    string Name,
    string? MuscleGroup = null,
    string? Equipment = null,
    IReadOnlyList<string>? Aliases = null
);
