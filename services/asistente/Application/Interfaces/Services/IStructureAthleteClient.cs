namespace Application.Interfaces.Services;

public sealed record StructureAthleteSummaryDto(
    Guid Id,
    string FullName,
    Guid? CategoryId = null,
    bool Active = true,
    Guid PfId = default,
    IReadOnlyList<Guid>? CategoryIds = null)
{
    /// <summary>
    /// Devuelve todas las categorías activas asignadas al atleta, consolidando 'CategoryIds' o 'CategoryId' para compatibilidad (#182 · KAN-477).
    /// </summary>
    public IReadOnlyList<Guid> AllCategoryIds
    {
        get
        {
            if (CategoryIds is { Count: > 0 })
            {
                return CategoryIds.Where(id => id != Guid.Empty).Distinct().ToList();
            }

            return CategoryId.HasValue && CategoryId.Value != Guid.Empty
                ? [CategoryId.Value]
                : [];
        }
    }
}

/// <summary>
/// Estado del permiso del tutor para recomendar hábitos y alimentación a un atleta (#201 · KAN-496).
/// </summary>
public sealed record StructureAthleteHabitsPermissionDto(
    bool IsMinor,
    bool AllowsHabitsRecommendations);

public interface IStructureAthleteClient
{
    /// <summary>
    /// Verifica si un atleta existe y pertenece al preparador físico autenticado (KAN-423 / KAN-431).
    /// </summary>
    Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene si el atleta es menor de edad y si su tutor autorizó las recomendaciones de hábitos (#201 · KAN-496).
    /// Devuelve <c>null</c> cuando Estructura no responde (404, 5xx, timeout) o el atleta no existe:
    /// el consumidor debe asumir que no hay permiso y no generar recomendación (fail-closed).
    /// </summary>
    Task<StructureAthleteHabitsPermissionDto?> GetHabitsPermissionAsync(Guid athleteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la lista de atletas activos para el análisis del worker nocturno (#177 · KAN-472).
    /// </summary>
    Task<IReadOnlyList<StructureAthleteSummaryDto>> GetAthletesAsync(CancellationToken cancellationToken = default);
}
