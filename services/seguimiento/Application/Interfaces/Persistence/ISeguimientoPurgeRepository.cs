using Application.DTOs.Internal;

namespace Application.Interfaces.Persistence;

/// <summary>
/// Repositorio para la purga física completa de datos de un atleta en Seguimiento (#204 · KAN-499).
/// </summary>
public interface ISeguimientoPurgeRepository
{
    Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default);
}

