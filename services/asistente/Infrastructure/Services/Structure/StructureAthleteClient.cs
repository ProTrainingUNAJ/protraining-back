using System.Net;
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
        if (athleteId == Guid.Empty || httpClient.BaseAddress is null)
            return false;

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
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Error al comunicarse con Estructura para verificar el atleta {AthleteId}: {Message}",
                athleteId,
                ex.Message);
            return false;
        }
    }
}

