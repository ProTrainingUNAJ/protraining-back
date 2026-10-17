using Application.DTOs.Memberships;

namespace Application.Interfaces.Services;

// Consultas a Estructura por HTTP, con el token del usuario de la petición.
public interface IStructureClient
{
    // Categorías activas del atleta logueado. Lista vacía si su cuenta no está vinculada a un atleta.
    Task<IReadOnlyList<AthleteCategoryDto>> GetMyCategoriesAsync(CancellationToken cancellationToken = default);
    Task<AthleteMeDto?> GetMeAsync(CancellationToken cancellationToken = default);
}
