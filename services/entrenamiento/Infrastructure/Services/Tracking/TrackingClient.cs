using Application.DTOs.Tracking;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace Infrastructure.Services.Tracking;

public sealed class TrackingClient(HttpClient httpClient, ILogger<TrackingClient> logger) : ITrackingClient
{
    public async Task<IReadOnlyList<TrackingBestMarkDto>> GetAthleteBestMarksAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        var path = $"api/v1/athletes/{athleteId}/fitness-tests/best-marks";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            using var response = await httpClient.GetAsync(path, timeoutCts.Token);

            if (response.StatusCode == HttpStatusCode.OK)
                return await response.Content.ReadFromJsonAsync<List<TrackingBestMarkDto>>(timeoutCts.Token) ?? [];

            logger.LogWarning(
                "Seguimiento respondió {Status} al consultar mejores marcas del atleta {AthleteId}.",
                (int)response.StatusCode,
                athleteId);
            return [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Seguimiento no respondió al consultar mejores marcas del atleta {AthleteId}.", athleteId);
            return [];
        }
    }
}
