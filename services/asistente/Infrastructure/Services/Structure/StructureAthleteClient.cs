using System.Net;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP para validar la pertenencia y existencia de atletas en Estructura (KAN-423 / KAN-431).
/// El aislamiento multi-tenant por PfId se propaga con el token del PF a través de ForwardAuthorizationHandler.
/// </summary>
public sealed class StructureAthleteClient(
    HttpClient httpClient,
    ILogger<StructureAthleteClient> logger) : IStructureAthleteClient
{
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
}
