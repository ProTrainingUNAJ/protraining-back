namespace Application.Interfaces.Services;

using Application.DTOs.Ai;

/// <summary>
/// Contrato para la consulta y verificación de existencia de ejercicios dentro del catálogo del club.
/// </summary>
public interface IExerciseCatalogService
{
    /// <summary>
    /// Busca un ejercicio por su identificador único exacto.
    /// </summary>
    Task<CatalogExerciseDto?> FindByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca un ejercicio por su nombre aplicando coincidencia normalizada, insensible a mayúsculas, minúsculas, tildes y alias comunes.
    /// </summary>
    Task<CatalogExerciseDto?> FindByNameAsync(string exerciseName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Realiza una búsqueda por término en el catálogo.
    /// </summary>
    Task<IReadOnlyList<CatalogExerciseDto>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene todos los ejercicios disponibles en el catálogo.
    /// </summary>
    Task<IReadOnlyList<CatalogExerciseDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta ejercicios en el catálogo de Entrenamiento filtrando opcionalmente por grupo muscular y término de búsqueda (#158 · KAN-563).
    /// </summary>
    Task<IReadOnlyList<CatalogExerciseDto>> GetExercisesAsync(
        string? muscleGroup,
        string? search,
        int pageSize = 50,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogExerciseDto>>([]);

    /// <summary>
    /// Obtiene ejercicios contextualizados según el objetivo deportivo solicitado (#158 · KAN-563).
    /// Prioriza ejercicios compuestos básicos (sentadilla, peso muerto, zancada, prensa / press de banca, press militar / remo, dominadas)
    /// mediante búsqueda directa en el catálogo de Entrenamiento antes de completar por grupo muscular.
    /// </summary>
    Task<IReadOnlyList<CatalogExerciseDto>> GetExercisesForGoalAsync(
        string? goal,
        int maxExercises = 25,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogExerciseDto>>([]);
}
