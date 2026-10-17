using Application.DTOs.Structure;

namespace Application.Interfaces.Services;

// Consultas a Estructura por HTTP, con el token del usuario de la petición.
public interface IStructureClient
{
    // Atleta del usuario autenticado; null si su cuenta no está vinculada a un atleta.
    Task<AthleteMeDto?> GetMeAsync(CancellationToken cancellationToken = default);
}