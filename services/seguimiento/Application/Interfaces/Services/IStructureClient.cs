using Application.DTOs.Structure;

namespace Application.Interfaces.Services;

// Consultas a Estructura por HTTP, con el token del usuario de la petición.
public interface IStructureClient
{
    // Atleta del usuario autenticado; null si su cuenta no está vinculada a un atleta.
    Task<AthleteMeDto?> GetMeAsync(CancellationToken cancellationToken = default);

    // Atletas de una categoría (rol PreparadorFisico); lanza CategoryNotFoundException si la categoría no existe.
    Task<IReadOnlyList<CategoryAthleteDto>> GetCategoryAthletesAsync(Guid categoryId, CancellationToken cancellationToken = default);

    // true si el atleta existe y es del PF del token (rol PreparadorFisico); false si no existe o es de otro PF.
    Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
}