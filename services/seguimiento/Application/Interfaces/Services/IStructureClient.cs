using Application.DTOs.Structure;

namespace Application.Interfaces.Services;

// Consultas a Estructura por HTTP, con el token del usuario de la petición.
public interface IStructureClient
{
    // Atleta del usuario autenticado; null si su cuenta no está vinculada a un atleta.
    Task<AthleteMeDto?> GetMeAsync(CancellationToken cancellationToken = default);

    // Atletas de una categoría (rol PreparadorFisico); lanza CategoryNotFoundException si la categoría no existe.
    Task<IReadOnlyList<CategoryAthleteDto>> GetCategoryAthletesAsync(Guid categoryId, CancellationToken cancellationToken = default);

    // Atletas de una categoría por el endpoint interno de Estructura, con token de servicio (KAN-578).
    // Para cuando el que pide no es el PF (ej.: la comparación del atleta con su grupo).
    Task<IReadOnlyList<CategoryAthleteDto>> GetCategoryAthletesInternalAsync(Guid categoryId, CancellationToken cancellationToken = default);

    // true si la categoría existe y es del PF del token (rol PreparadorFisico); false si no existe o es de otro PF.
    Task<bool> IsOwnCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    // true si el atleta existe y es del PF del token (rol PreparadorFisico); false si no existe o es de otro PF.
    Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
}