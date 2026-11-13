using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Cliente HTTP hacia Seguimiento para consultar check-ins diarios de wellness
/// </summary>
public sealed class WellnessCheckInService(
    HttpClient httpClient,
    ILogger<WellnessCheckInService> logger) : IWellnessCheckInService
{
    public const string CompletedCheckInsPath = "api/v1/internal/check-ins/done";

    public sealed record CheckInsDoneRequest(DateOnly Date, Guid? PfId, IReadOnlyList<Guid> AthleteIds);

    public async Task<bool> HasCompletedCheckInAsync(
        Guid athleteId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var completed = await GetCompletedCheckInAthleteIdsAsync([athleteId], date, null, cancellationToken);
        return completed.Contains(athleteId);
    }

    public async Task<IReadOnlySet<Guid>> GetCompletedCheckInAthleteIdsAsync(
        IReadOnlyList<Guid> athleteIds,
        DateOnly date,
        Guid? pfId = null,
        CancellationToken cancellationToken = default)
    {
        if (athleteIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var payload = new CheckInsDoneRequest(date, pfId, athleteIds);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(CompletedCheckInsPath, payload, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var doneIds = await response.Content.ReadFromJsonAsync<List<Guid>>(cancellationToken: cancellationToken);
                return doneIds?.ToHashSet() ?? new HashSet<Guid>();
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new HashSet<Guid>();
            }

            logger.LogWarning("Seguimiento respondió {StatusCode} al consultar check-ins completados para la fecha {Date}.", response.StatusCode, date);
            return new HashSet<Guid>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Resiliencia: si Seguimiento no responde, se loguea y el worker continua sin caerse
            logger.LogWarning(ex, "Seguimiento no responde o no está disponible al consultar check-ins para {Date}.", date);
            return new HashSet<Guid>();
        }
    }
}


