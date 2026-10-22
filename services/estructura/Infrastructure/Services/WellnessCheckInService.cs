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
/// cliente HTTP hacia Seguimiento para consultar check-ins diarios de wellness
/// </summary>
public sealed class WellnessCheckInService(
    HttpClient httpClient,
    ILogger<WellnessCheckInService> logger) : IWellnessCheckInService
{
    public const string AthleteCheckInsPath = "api/v1/athletes/{0}/check-ins";

    private sealed record CheckInDto(DateOnly Date);

    public async Task<bool> HasCompletedCheckInAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var url = $"{string.Format(AthleteCheckInsPath, athleteId)}?date={date:yyyy-MM-dd}&limit=1";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var checkIns = await response.Content.ReadFromJsonAsync<List<CheckInDto>>(cancellationToken: cancellationToken);
                return checkIns?.Exists(c => c.Date == date) ?? false;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            logger.LogWarning("Seguimiento respondio {StatusCode} al consultar check-in del atleta {AthleteId}.", response.StatusCode, athleteId);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // resiliencia: si Seguimiento no responde, se loguea y el worker no se interrumpe
            logger.LogWarning(ex, "Seguimiento no responde o no esta disponible al consultar check-in del atleta {AthleteId}.", athleteId);
            return false;
        }
    }
}

