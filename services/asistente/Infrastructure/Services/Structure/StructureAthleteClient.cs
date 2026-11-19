using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP para validar la pertenencia y existencia de atletas en Estructura (KAN-423 / KAN-431 / KAN-472 / KAN-491 / KAN-496).
/// El aislamiento multi-tenant por PfId se propaga con el token del PF o token de Servicio a través de ForwardAuthorizationHandler.
/// </summary>
public sealed class StructureAthleteClient(
    HttpClient httpClient,
    ILogger<StructureAthleteClient> logger) : IStructureAthleteClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record StructureAthleteApiResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("pfId")] Guid PfId,
        [property: JsonPropertyName("fullName")] string? FullName,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("active")] bool Active,
        [property: JsonPropertyName("categoryId")] Guid? CategoryId,
        [property: JsonPropertyName("categoryIds")] IReadOnlyList<Guid>? CategoryIds = null);

    public async Task<bool> IsOwnAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            return false;

        if (httpClient.BaseAddress is null)
            throw new StructureServiceUnavailableException("La dirección base del microservicio de Estructura no está configurada.");

        try
        {
            var url = $"api/v1/athletes/{athleteId}";
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.OK)
                return true;

            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;

            logger.LogWarning(
                "Estructura respondió código HTTP {StatusCode} al verificar el atleta {AthleteId}.",
                (int)response.StatusCode,
                athleteId);

            throw new StructureServiceUnavailableException(
                $"El microservicio de Estructura no se encuentra disponible (código {(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructureServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Error al comunicarse con Estructura para verificar el atleta {AthleteId}: {Message}",
                athleteId,
                ex.Message);

            throw new StructureServiceUnavailableException(
                "El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }

    private sealed record StructureAthletePermissionApiResponse(
        [property: JsonPropertyName("isMinor")] bool IsMinor,
        [property: JsonPropertyName("allowsHabitsRecommendations")] bool AllowsHabitsRecommendations);

    public async Task<StructureAthleteHabitsPermissionDto?> GetHabitsPermissionAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            return null;

        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada.");
            return null;
        }

        try
        {
            var url = $"api/v1/athletes/{athleteId}";
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Estructura respondió código HTTP {StatusCode} al consultar el permiso del tutor del atleta {AthleteId}. No se generará recomendación de alimentación (fail-closed).",
                    (int)response.StatusCode,
                    athleteId);
                return null;
            }

            var athlete = await response.Content.ReadFromJsonAsync<StructureAthletePermissionApiResponse>(cancellationToken: cancellationToken);
            if (athlete is null)
            {
                logger.LogWarning(
                    "Estructura devolvió una respuesta vacía al consultar el permiso del tutor del atleta {AthleteId}. No se generará recomendación de alimentación (fail-closed).",
                    athleteId);
                return null;
            }

            return new StructureAthleteHabitsPermissionDto(athlete.IsMinor, athlete.AllowsHabitsRecommendations);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error al comunicarse con Estructura para consultar el permiso del tutor del atleta {AthleteId}. No se generará recomendación de alimentación (fail-closed).",
                athleteId);
            return null;
        }
    }

    public async Task<IReadOnlyList<StructureAthleteSummaryDto>> GetAthletesAsync(CancellationToken cancellationToken = default)
    {
        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada.");
            throw new StructureServiceUnavailableException("La dirección base del microservicio de Estructura no está configurada.");
        }

        try
        {
            const string url = "api/v1/internal/athletes";
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Estructura respondió código HTTP {StatusCode} al obtener atletas para el worker nocturno.",
                    (int)response.StatusCode);

                throw new StructureServiceUnavailableException(
                    $"El microservicio de Estructura no se encuentra disponible (código {(int)response.StatusCode}).");
            }

            var apiAthletes = await response.Content.ReadFromJsonAsync<List<StructureAthleteApiResponse>>(cancellationToken: cancellationToken);
            if (apiAthletes is null || apiAthletes.Count == 0)
            {
                return [];
            }

            return apiAthletes
                .Where(a => a.Active)
                .Select(a =>
                {
                    var name = !string.IsNullOrWhiteSpace(a.FullName)
                        ? a.FullName.Trim()
                        : $"{a.FirstName} {a.LastName}".Trim();

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "Atleta " + a.Id.ToString()[..8];
                    }

                    return new StructureAthleteSummaryDto(
                        Id: a.Id,
                        FullName: name,
                        CategoryId: a.CategoryId,
                        Active: a.Active,
                        PfId: a.PfId,
                        CategoryIds: a.CategoryIds);
                })
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (StructureServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al comunicarse con Estructura para obtener atletas.");
            throw new StructureServiceUnavailableException("El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }

    public async Task<IReadOnlyList<StructureCategoryDto>> GetAthleteCategoriesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no pertenece al preparador físico autenticado.");

        var result = await GetFromStructureAsync<List<StructureCategoryDto>>(
            relativeUrl: $"api/v1/athletes/{athleteId}/categories",
            returnNullOn404: false,
            notFoundMessage: "El atleta no existe o no pertenece al preparador físico autenticado.",
            operationDescription: $"consultar las categorías del atleta {athleteId}",
            cancellationToken: cancellationToken);

        return result ?? [];
    }

    public Task<StructureCategoryDto?> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        if (categoryId == Guid.Empty)
            return Task.FromResult<StructureCategoryDto?>(null);

        return GetFromStructureAsync<StructureCategoryDto>(
            relativeUrl: $"api/v1/categories/{categoryId}",
            returnNullOn404: true,
            notFoundMessage: "La categoría no existe o no pertenece al preparador físico autenticado.",
            operationDescription: $"consultar la categoría {categoryId}",
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<StructureCategoryAthleteDto>> GetCategoryAthletesAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        if (categoryId == Guid.Empty)
            throw new NotFoundException("La categoría no existe o no pertenece al preparador físico autenticado.");

        var athletes = await GetFromStructureAsync<List<StructureCategoryAthleteDto>>(
            relativeUrl: $"api/v1/categories/{categoryId}/athletes",
            returnNullOn404: false,
            notFoundMessage: "La categoría no existe o no pertenece al preparador físico autenticado.",
            operationDescription: $"consultar los atletas de la categoría {categoryId}",
            cancellationToken: cancellationToken);

        return athletes?
            .Where(a => a.Active && a.Id != Guid.Empty)
            .ToList()
            ?? [];
    }

    private async Task<T?> GetFromStructureAsync<T>(
        string relativeUrl,
        bool returnNullOn404,
        string notFoundMessage,
        string operationDescription,
        CancellationToken cancellationToken) where T : class
    {
        if (httpClient.BaseAddress is null)
        {
            logger.LogError("La dirección base de Estructura no está configurada.");
            throw new StructureServiceUnavailableException("La dirección base del microservicio de Estructura no está configurada.");
        }

        try
        {
            using var response = await httpClient.GetAsync(relativeUrl, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (returnNullOn404)
                    return null;

                throw new NotFoundException(notFoundMessage);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException(
                    $"Estructura rechazó la autenticación o permisos al {operationDescription}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Estructura respondió código HTTP {StatusCode} al {Operation}.",
                    (int)response.StatusCode,
                    operationDescription);
                throw new StructureServiceUnavailableException(
                    $"El microservicio de Estructura no se encuentra disponible (código {(int)response.StatusCode}).");
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (StructureServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al comunicarse con Estructura al {Operation}.", operationDescription);
            throw new StructureServiceUnavailableException("El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }
}
