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
}
