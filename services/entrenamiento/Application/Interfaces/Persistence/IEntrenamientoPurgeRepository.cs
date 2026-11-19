using Application.DTOs.Internal;

namespace Application.Interfaces.Persistence;

/// <summary>
/// Repositorio para la purga física completa de datos de un atleta en Entrenamiento (#204 · KAN-499).
/// </summary>
public interface IEntrenamientoPurgeRepository
{
    Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default);
}

