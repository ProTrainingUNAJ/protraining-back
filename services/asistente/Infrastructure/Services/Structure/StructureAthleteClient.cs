using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP para validar la pertenencia y existencia de atletas en Estructura (KAN-423 / KAN-431 / KAN-472).
/// El aislamiento multi-tenant por PfId se propaga con el token del PF o token de Servicio a través de ForwardAuthorizationHandler.
/// </summary>
public sealed class StructureAthleteClient(
    HttpClient httpClient,
    ILogger<StructureAthleteClient> logger) : IStructureAthleteClient
{
    private sealed record StructureAthleteApiResponse(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("pfId")] Guid PfId,
        [property: JsonPropertyName("fullName")] string? FullName,
        [property: JsonPropertyName("firstName")] string? FirstName,
        [property: JsonPropertyName("lastName")] string? LastName,
        [property: JsonPropertyName("active")] bool Active,
        [property: JsonPropertyName("categoryId")] Guid? CategoryId);

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

                    return new StructureAthleteSummaryDto(a.Id, name, a.CategoryId, a.Active, a.PfId);
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
}
