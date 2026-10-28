using Application.DTOs.Memberships;

namespace Application.Interfaces.Services;

// Consultas a Estructura por HTTP, con el token del usuario de la petición.
public interface IStructureClient
{
    // Categorías activas del atleta logueado. Lista vacía si su cuenta no está vinculada a un atleta.
    Task<IReadOnlyList<AthleteCategoryDto>> GetMyCategoriesAsync(CancellationToken cancellationToken = default);
    Task<AthleteMeDto?> GetMeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AthleteCategoryDto>> GetAthleteCategoriesAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task<StructureAthleteDto?> GetAthleteByIdAsync(Guid athleteId, CancellationToken cancellationToken = default);

    // true si la categoría existe y es del PF del token; false si no existe o es de otro PF (KAN-431).
    Task<bool> IsOwnCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
