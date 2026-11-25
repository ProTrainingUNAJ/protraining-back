using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Identity;

/// <summary>
/// Cliente HTTP para consultar en Identidad la preferencia de recomendaciones de hábitos y alimentación (KAN-493).
/// Reenvía el token del solicitante mediante ForwardAuthorizationHandler: un PF consulta su propia preferencia
/// y un atleta consulta la de su PF. Ante cualquier fallo devuelve null para no habilitar recomendaciones por defecto.
/// </summary>
public sealed class IdentityHabitsRecommendationsClient(
    HttpClient httpClient,
    ILogger<IdentityHabitsRecommendationsClient> logger) : IIdentityHabitsRecommendationsClient
{
    private sealed record HabitsRecommendationsStatusResponse(
        [property: JsonPropertyName("enabled")] bool Enabled);

    public async Task<bool?> GetMyHabitsRecommendationsEnabledAsync(CancellationToken cancellationToken = default) =>
        await GetEnabledAsync(
            "api/v1/users/me/habits-recommendations",
            "del preparador físico autenticado",
            cancellationToken);

    public async Task<bool?> GetAthletePfHabitsRecommendationsEnabledAsync(CancellationToken cancellationToken = default) =>
        await GetEnabledAsync(
            "api/v1/users/me/pf-habits-recommendations",
            "del preparador físico del atleta autenticado",
            cancellationToken);

    private async Task<bool?> GetEnabledAsync(
        string relativeUrl,
        string preferenceDescription,
        CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null)
        {
            logger.LogError(
                "La dirección base de Identidad no está configurada. No se habilitarán recomendaciones (fail-closed).");
            return null;
        }

        try
        {
            using var response = await httpClient.GetAsync(relativeUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Identidad respondió código HTTP {StatusCode} al consultar la preferencia {PreferenceDescription}. No se habilitarán recomendaciones (fail-closed).",
                    (int)response.StatusCode,
                    preferenceDescription);
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<HabitsRecommendationsStatusResponse>(
                cancellationToken: cancellationToken);

            if (status is null)
            {
                logger.LogWarning(
                    "Identidad devolvió una respuesta vacía al consultar la preferencia {PreferenceDescription}. No se habilitarán recomendaciones (fail-closed).",
                    preferenceDescription);
                return null;
            }

            return status.Enabled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Error al comunicarse con Identidad para consultar la preferencia {PreferenceDescription}. No se habilitarán recomendaciones (fail-closed).",
                preferenceDescription);
            return null;
        }
    }
}
