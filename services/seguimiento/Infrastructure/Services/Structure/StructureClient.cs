using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Polly;

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
        // KAN-367: además de las caídas de red, el pipeline de resiliencia (Polly v8, aplicado en
        // Program.cs con AddStandardResilienceHandler) puede rechazar la ejecución con
        // BrokenCircuitException o TimeoutRejectedException (base común: ExecutionRejectedException).
        // Sin este catch esas excepciones llegaban al middleware como 500; ahora degradan a
        // StructureUnavailableException y el caso de uso responde 200 con los datos locales.
        catch (Exception exception) when (exception is HttpRequestException
                                          or TaskCanceledException
                                          or ExecutionRejectedException)
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

    public async Task<IReadOnlyList<CategoryAthleteDto>> GetCategoryAthletesAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync($"api/v1/categories/{categoryId}/athletes", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        // KAN-367: mismo criterio que GetMeAsync: las rejecciones del pipeline de resiliencia
        // (breaker abierto o timeout) deben degradar a 200, nunca escapar como 500.
        catch (Exception exception) when (exception is HttpRequestException
                                          or TaskCanceledException
                                          or ExecutionRejectedException)
        {
            throw new StructureUnavailableException(exception);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await response.Content.ReadFromJsonAsync<List<CategoryAthleteDto>>(cancellationToken) ?? [];

                // La categoría no existe en Estructura: lo resuelve el controller con 404.
                case HttpStatusCode.NotFound:
                    logger.LogWarning("La categoría {CategoryId} no existe en Estructura.", categoryId);
                    throw new CategoryNotFoundException();

                case HttpStatusCode.Unauthorized:
                    throw new UnauthorizedAccessException();

                // Estructura exige el rol PreparadorFisico: sin él, 403.
                case HttpStatusCode.Forbidden:
                    throw new ForbiddenException("La cuenta del token no está autorizada como preparador físico.");

                default:
                    logger.LogError("Estructura respondió {Status} al consultar los atletas de la categoría {CategoryId}.",
                        (int)response.StatusCode, categoryId);
                    throw new StructureUnavailableException();
            }
        }
    }
}