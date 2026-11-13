using Application.DTOs.Alerts;
using Application.DTOs.Adaptation;
using Application.DTOs.DayConflicts;

namespace Application.Interfaces.Services;

/// <summary>
/// Contrato de comunicación HTTP hacia el microservicio de Seguimiento (#177 · KAN-472 / #156 · KAN-451).
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

    /// <summary>
    /// Obtiene el check-in de hoy del atleta desde Seguimiento (#156 · KAN-451).
    /// Devuelve null si Seguimiento responde 404 (todavía no hizo el check-in).
    /// Lanza SeguimientoServiceUnavailableException (503) si Seguimiento no responde.
    /// </summary>
    Task<TodayCheckInDto?> GetTodayCheckInAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActiveInjuryRemoteDto>> GetActiveInjuriesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);
}
