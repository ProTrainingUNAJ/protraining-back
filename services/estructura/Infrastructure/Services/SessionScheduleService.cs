using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.DTOs.Reminders;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Cliente HTTP hacia Entrenamiento para consultar atletas y categorías con sesión programada
/// </summary>
public sealed class SessionScheduleService(
    HttpClient httpClient,
    ILogger<SessionScheduleService> logger) : ISessionScheduleService
{
    public const string ScheduledAthletesPath = "api/v1/internal/training-sessions/athletes-with-session";

    private static readonly ScheduledSessionTargetsResponse EmptyResponse =
        new(Array.Empty<Guid>(), Array.Empty<Guid>());

    public async Task<ScheduledSessionTargetsResponse> GetAthletesWithScheduledSessionAsync(
        DateOnly date,
        Guid pfId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{ScheduledAthletesPath}?date={date:yyyy-MM-dd}&pfId={pfId}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var targets = await response.Content.ReadFromJsonAsync<ScheduledSessionTargetsResponse>(cancellationToken: cancellationToken);
                return targets ?? EmptyResponse;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("No se encontraron sesiones programadas en Entrenamiento para la fecha {Date} y PF {PfId}.", date, pfId);
                return EmptyResponse;
            }

            logger.LogWarning("Entrenamiento respondió {StatusCode} al consultar sesiones de la fecha {Date} y PF {PfId}.", response.StatusCode, date, pfId);
            return EmptyResponse;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Resiliencia: si Entrenamiento no responde, el worker no se cae, loguea y continúa
            logger.LogWarning(ex, "Entrenamiento no responde o no está disponible al consultar sesiones para {Date} y PF {PfId}.", date, pfId);
            return EmptyResponse;
        }
    }
}
