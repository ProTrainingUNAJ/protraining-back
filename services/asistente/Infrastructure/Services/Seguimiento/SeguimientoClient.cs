using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Alerts;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Seguimiento;

/// <summary>
/// Cliente HTTP hacia Seguimiento para consultar check-ins y fatiga acumulada (#177 · KAN-472).
/// Diseñado con resiliencia defensiva: ante fallos o indisponibilidad de red, loguea la advertencia
/// y retorna una colección vacía sin interrumpir el ciclo del worker nocturno.
/// </summary>
public sealed class SeguimientoClient(
    HttpClient httpClient,
    ILogger<SeguimientoClient> logger) : ISeguimientoClient
{
    public const string HistoryPath = "api/v1/internal/check-ins/history";

    public sealed record CheckInsHistoryRequest(
        IReadOnlyList<Guid> AthleteIds,
        DateOnly StartDate,
        DateOnly EndDate);

    public async Task<IReadOnlyList<AthleteCheckInHistoryItemDto>> GetAthleteCheckInsHistoryAsync(
        IReadOnlyCollection<Guid> athleteIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (athleteIds.Count == 0 || httpClient.BaseAddress is null)
        {
            return [];
        }

        try
        {
            var payload = new CheckInsHistoryRequest(athleteIds.ToList(), from, to);
            using var response = await httpClient.PostAsJsonAsync(HistoryPath, payload, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var history = await response.Content.ReadFromJsonAsync<List<AthleteCheckInHistoryItemDto>>(cancellationToken: cancellationToken);
                return history ?? [];
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            logger.LogWarning(
                "Seguimiento respondió código {StatusCode} al consultar el historial de check-ins.",
                (int)response.StatusCode);
            return [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error al comunicarse con el microservicio de Seguimiento para consultar check-ins ({FromDate} a {ToDate}).",
                from,
                to);
            return [];
        }
    }
}

