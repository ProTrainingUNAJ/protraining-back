using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// cliente HTTP hacia Entrenamiento para consultar atletas con sesion programada
/// </summary>
public sealed class SessionScheduleService(
    HttpClient httpClient,
    ILogger<SessionScheduleService> logger) : ISessionScheduleService
{
    public const string ScheduledAthletesPath = "api/v1/training-sessions/scheduled-athletes";

    public async Task<IReadOnlyList<Guid>> GetAthletesWithScheduledSessionAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var url = $"{ScheduledAthletesPath}?date={date:yyyy-MM-dd}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var athleteIds = await response.Content.ReadFromJsonAsync<List<Guid>>(cancellationToken: cancellationToken);
                return athleteIds ?? (IReadOnlyList<Guid>)Array.Empty<Guid>();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("No se encontraron sesiones programadas en Entrenamiento para la fecha {Date}.", date);
                return Array.Empty<Guid>();
            }

            logger.LogWarning("Entrenamiento respondio {StatusCode} al consultar sesiones de la fecha {Date}.", response.StatusCode, date);
            return Array.Empty<Guid>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // resiliencia: si Entrenamiento no responde, el worker no se cae, loguea y continua
            logger.LogWarning(ex, "Entrenamiento no responde o no esta disponible al consultar sesiones para {Date}.", date);
            return Array.Empty<Guid>();
        }
    }
}
