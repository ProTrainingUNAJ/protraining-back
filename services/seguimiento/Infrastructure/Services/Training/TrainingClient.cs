using System.Net.Http.Json;
using Application.DTOs.Training;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Training;

public sealed class TrainingClient(
    HttpClient httpClient,
    ILogger<TrainingClient> logger) : ITrainingClient
{
    public async Task<IReadOnlyList<AthleteRecordRemoteDto>> GetAthleteRecordsAsync(
        Guid athleteId,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/athletes/{athleteId}/records?currentOnly=false";
        if (from.HasValue)
            url += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue)
            url += $"&to={to.Value:yyyy-MM-dd}";

        try
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "El microservicio de Entrenamiento respondió con status {StatusCode} al obtener los récords del atleta {AthleteId}.",
                    response.StatusCode,
                    athleteId);
                return [];
            }

            var records = await response.Content.ReadFromJsonAsync<List<AthleteRecordRemoteDto>>(cancellationToken);
            return records ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error de red o comunicación con el microservicio de Entrenamiento al obtener récords del atleta {AthleteId}.",
                athleteId);
            return [];
        }
    }
}

