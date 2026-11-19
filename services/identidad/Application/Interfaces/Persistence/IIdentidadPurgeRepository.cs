using Application.DTOs.Internal;

namespace Application.Interfaces.Persistence;

/// <summary>
/// Repositorio para la purga física completa de datos de un usuario/atleta en Identidad (#204 · KAN-499).
/// </summary>
public interface IIdentidadPurgeRepository
{
    Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default);
}
