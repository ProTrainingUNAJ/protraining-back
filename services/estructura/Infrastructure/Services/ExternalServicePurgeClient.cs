using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class ExternalServicePurgeClient(
    HttpClient httpClient,
    ILogger<ExternalServicePurgeClient> logger) : IExternalServicePurgeClient
{
    public const string PurgeEndpointTemplate = "api/v1/internal/athletes/{0}/purge";

    public async Task<ServicePurgeResult> PurgeAthleteInServiceAsync(
        string serviceName,
        string baseUrl,
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        var relativeUrl = string.Format(PurgeEndpointTemplate, athleteId);
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        var requestUri = new Uri(baseUri, relativeUrl);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            // 200 OK o 204 No Content -> Purga exitosa
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Purga del atleta {AthleteId} confirmada en el servicio {ServiceName}.", athleteId, serviceName);
                return new ServicePurgeResult(serviceName, true, (int)response.StatusCode);
            }

            // 404 Not Found -> Es un fallo: indica ruta/servicio inexistente o URL mal configurada.
            // Ningún endpoint de purga responde 404 cuando no hay datos; todos devuelven 200 con ceros.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var notFoundMsg = $"HTTP 404: Endpoint no encontrado en {serviceName} ({requestUri}). Verifique la URL base y el despliegue del servicio.";
                logger.LogWarning(
                    "El servicio {ServiceName} respondió 404 Not Found al purgar atleta {AthleteId} en {RequestUri}. Se considera fallo.",
                    serviceName,
                    athleteId,
                    requestUri);
                return new ServicePurgeResult(serviceName, false, 404, notFoundMsg);
            }

            // Códigos 4xx (excepto 404) y 5xx
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "El servicio {ServiceName} respondió {StatusCode} al purgar al atleta {AthleteId}: {Body}",
                serviceName,
                (int)response.StatusCode,
                athleteId,
                errorBody);

            return new ServicePurgeResult(
                serviceName,
                false,
                (int)response.StatusCode,
                $"HTTP {(int)response.StatusCode}: {errorBody}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "El servicio {ServiceName} no responde o está caído al purgar atleta {AthleteId}.", serviceName, athleteId);
            return new ServicePurgeResult(serviceName, false, null, ex.Message);
        }
    }
}

