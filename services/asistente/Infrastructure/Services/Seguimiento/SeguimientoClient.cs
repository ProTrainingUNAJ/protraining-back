using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Adaptation;
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

            logger.LogError(
                "Seguimiento respondió código {StatusCode} al consultar el historial de check-ins.",
                (int)response.StatusCode);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException(
                    "Seguimiento rechazó la autenticación del servicio al consultar el historial de check-ins.");
            }

            throw new SeguimientoServiceUnavailableException(
                $"Seguimiento no pudo consultar el historial de check-ins (código {(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (SeguimientoServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error al comunicarse con el microservicio de Seguimiento para consultar check-ins ({FromDate} a {ToDate}).",
                from,
                to);
            throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage, ex);
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

    public async Task<IReadOnlyList<ActiveInjuryRemoteDto>> GetActiveInjuriesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty || httpClient.BaseAddress is null)
            throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage);

        try
        {
            using var response = await httpClient.GetAsync(
                $"api/v1/athletes/{athleteId}/injuries?status=Active",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new NotFoundException(
                    "El atleta no existe o no pertenece al preparador físico autenticado.");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException(
                    "Seguimiento rechazó la autenticación o permisos al consultar lesiones.");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Seguimiento respondió {StatusCode} al consultar lesiones del atleta {AthleteId}.",
                    (int)response.StatusCode,
                    athleteId);
                throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage);
            }

            return await response.Content.ReadFromJsonAsync<List<ActiveInjuryRemoteDto>>(
                       JsonOptions,
                       cancellationToken)
                   ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SeguimientoServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error al consultar lesiones del atleta {AthleteId} en Seguimiento.",
                athleteId);
            throw new SeguimientoServiceUnavailableException(SeguimientoUnavailableMessage, ex);
        }
    }
}
