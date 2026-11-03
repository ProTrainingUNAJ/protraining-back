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
    /// Consulta ejercicios en el catálogo de Entrenamiento filtrando opcionalmente por grupo muscular,
    /// término de búsqueda y agrupamiento de variantes (#158 · KAN-563 / KAN-554).
    /// </summary>
    Task<IReadOnlyList<CatalogExerciseDto>> GetExercisesAsync(
        string? muscleGroup,
        string? search,
        int pageSize = 50,
        bool groupVariants = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene ejercicios contextualizados según el objetivo deportivo solicitado (#158 · KAN-563).
    /// Prioriza ejercicios compuestos básicos (sentadilla con barra, prensa de piernas, empuje de cadera,
    /// press de banca, press militar, peso muerto con barra, remo con barra) mediante búsqueda directa en Entrenamiento.
    /// </summary>
    Task<IReadOnlyList<CatalogExerciseDto>> GetExercisesForGoalAsync(
        string? goal,
        int maxExercises = 25,
        CancellationToken cancellationToken = default);
}
