using Application.DTOs.Internal;

namespace Application.Interfaces.Persistence;

/// <summary>
/// Contrato para la purga física definitiva de datos de un atleta en Asistente (#205 · KAN-500).
/// </summary>
public interface IAsistentePurgeRepository
{
    Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default);
}
