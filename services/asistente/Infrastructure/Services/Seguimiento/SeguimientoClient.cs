using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Alerts;
using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Seguimiento;

/// <summary>
/// Cliente HTTP hacia Seguimiento para consultar check-ins y fatiga acumulada (#177 · KAN-472 / #156 · KAN-451).
/// </summary>
public sealed class SeguimientoClient(
    HttpClient httpClient,
    ILogger<SeguimientoClient> logger) : ISeguimientoClient
{
    public const string HistoryPath = "api/v1/internal/check-ins/history";
    public const string SeguimientoUnavailableMessage = "El microservicio de Seguimiento no se encuentra disponible.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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

    public async Task<TodayCheckInDto?> GetTodayCheckInAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
            throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync($"api/v1/athletes/{athleteId}/check-in/today", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Error de comunicación con Seguimiento al consultar el check-in de hoy del atleta {AthleteId}: {ErrorType}.",
                athleteId,
                ex.GetType().Name);
            throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage, ex);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    try
                    {
                        return await response.Content.ReadFromJsonAsync<TodayCheckInDto>(JsonOptions, cancellationToken)
                            ?? throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage);
                    }
                    catch (SeguimientoServiceUnavailableException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            "Error al interpretar la respuesta de Seguimiento para el check-in de hoy del atleta {AthleteId}: {ErrorType}.",
                            athleteId,
                            ex.GetType().Name);
                        throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage, ex);
                    }

                case HttpStatusCode.NotFound:
                    return null;

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                default:
                    logger.LogError(
                        "Seguimiento respondió {StatusCode} al consultar el check-in de hoy del atleta {AthleteId}.",
                        (int)response.StatusCode,
                        athleteId);
                    throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage);
            }
        }
    }
}
