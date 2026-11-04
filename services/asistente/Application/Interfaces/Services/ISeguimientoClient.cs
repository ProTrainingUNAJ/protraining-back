using Application.DTOs.Alerts;

namespace Application.Interfaces.Services;

/// <summary>
/// Contrato de comunicación HTTP hacia el microservicio de Seguimiento (#177 · KAN-472).
/// </summary>
public interface ISeguimientoClient
{
    /// <summary>
    /// Consulta el historial de check-ins de los atletas indicados dentro de una ventana temporal.
    /// </summary>
    Task<IReadOnlyList<AthleteCheckInHistoryItemDto>> GetAthleteCheckInsHistoryAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

