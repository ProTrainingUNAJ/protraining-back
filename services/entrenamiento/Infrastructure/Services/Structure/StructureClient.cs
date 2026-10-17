using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

// Cliente HTTP hacia Estructura. El token del usuario lo agrega el handler que se registra en la API.
public sealed class StructureClient(HttpClient httpClient, ILogger<StructureClient> logger) : IStructureClient
{
    public const string MyCategoriesPath = "api/v1/me/categories";
    public const string MePath = "api/v1/me";

    public async Task<IReadOnlyList<AthleteCategoryDto>> GetMyCategoriesAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(MyCategoriesPath, cancellationToken);
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
                    return await response.Content.ReadFromJsonAsync<List<AthleteCategoryDto>>(cancellationToken) ?? [];

                // La cuenta todavía no está vinculada a un atleta: no tiene categorías.
                case HttpStatusCode.NotFound:
                    logger.LogWarning("La cuenta del token no está vinculada a un atleta en Estructura.");
                    return [];

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException();

                default:
                    logger.LogError("Estructura respondió {Status} al pedir las categorías del atleta.", (int)response.StatusCode);
                    throw new StructureUnavailableException();
            }
        }
    }

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

                case HttpStatusCode.NotFound:
                    logger.LogWarning("La cuenta del token no está vinculada a un atleta en Estructura.");
                    return null;

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException();

                default:
                    logger.LogError("Estructura respondió {Status} al consultar el perfil del atleta.", (int)response.StatusCode);
                    throw new StructureUnavailableException();
            }
        }
    }

    public async Task<IReadOnlyList<AthleteCategoryDto>> GetAthleteCategoriesAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        var athleteCategoriesPath = $"api/v1/athletes/{athleteId}/categories";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(athleteCategoriesPath, cancellationToken);
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
                    return await response.Content.ReadFromJsonAsync<List<AthleteCategoryDto>>(cancellationToken) ?? [];

                case HttpStatusCode.NotFound:
                    logger.LogWarning("El atleta {AthleteId} no fue encontrado en Estructura al consultar sus categorías.", athleteId);
                    return [];

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException();

                default:
                    logger.LogError("Estructura respondió {Status} al pedir las categorías del atleta {AthleteId}.", (int)response.StatusCode, athleteId);
                    throw new StructureUnavailableException();
            }
        }
    }

    public async Task<StructureAthleteDto?> GetAthleteByIdAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        var path = $"api/v1/athletes/{athleteId}";
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(path, cancellationToken);
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
                    return await response.Content.ReadFromJsonAsync<StructureAthleteDto>(cancellationToken);
                case HttpStatusCode.NotFound:
                    logger.LogWarning("El atleta {AthleteId} no fue encontrado en Estructura.", athleteId);
                    return null;
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new UnauthorizedAccessException();
                default:
                    logger.LogError("Estructura respondió {Status} al consultar el detalle del atleta {AthleteId}.", (int)response.StatusCode, athleteId);
                    throw new StructureUnavailableException();
            }
        }
    }
}
