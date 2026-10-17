using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

// Cliente HTTP hacia Estructura. El token de la petición lo agrega ForwardAuthorizationHandler (en la API).
public sealed class StructureClient(HttpClient httpClient, ILogger<StructureClient> logger) : IStructureClient
{
    public const string MePath = "api/v1/me";

    public async Task<AthleteMeDto?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(MePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new StructureUnavailableException(exception);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await response.Content.ReadFromJsonAsync<AthleteMeDto>(cancellationToken);

                // La cuenta no está vinculada a un atleta: lo resuelve el controller con 404.
                case HttpStatusCode.NotFound:
                    logger.LogWarning("La cuenta del token no está vinculada a un atleta en Estructura.");
                    return null;

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                // Estructura exige el rol Atleta: sin él, 403.
                case HttpStatusCode.Forbidden:
                    throw new ForbiddenException("La cuenta del token no está autorizada como atleta.");

                default:
                    logger.LogError("Estructura respondió {Status} al consultar el perfil del atleta.", (int)response.StatusCode);
                    throw new StructureUnavailableException();
            }
        }
    }
}