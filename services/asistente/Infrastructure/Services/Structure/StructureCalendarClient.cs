using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.Services;
using Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP resiliente para consultar los partidos de una categoría en Estructura (KAN-406 / KAN-408 / KAN-473).
/// Utiliza el endpoint interno con rol 'Servicio' (#178 · KAN-473).
/// No silencia errores de autenticación o autorización (401/403).
/// </summary>
public sealed class StructureCalendarClient(
    HttpClient httpClient,
    ILogger<StructureCalendarClient> logger) : IStructureCalendarClient
{
    public sealed record CategoryCalendarEventDto(
        [property: JsonPropertyName("date")] DateOnly Date,
        [property: JsonPropertyName("type")] string? Type = null
    );

    public async Task<IReadOnlyList<DateOnly>> GetMatchDatesAsync(
        Guid categoryId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (categoryId == Guid.Empty || httpClient.BaseAddress is null)
            return [];

        try
        {
            var url = $"api/v1/internal/categories/{categoryId}/events?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                logger.LogError(
                    "Acceso denegado ({StatusCode}) al consultar el calendario interno en Estructura para la categoría {CategoryId}. Requiere rol 'Servicio' (#178 · KAN-473).",
                    (int)response.StatusCode,
                    categoryId);
                throw new StructureServiceUnavailableException(
                    $"Acceso denegado ({response.StatusCode}) en el endpoint interno de eventos de Estructura.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "No se pudo obtener el calendario de la categoría {CategoryId}. Código HTTP {StatusCode}.",
                    categoryId,
                    (int)response.StatusCode);
                return [];
            }

            var events = await response.Content.ReadFromJsonAsync<List<CategoryCalendarEventDto>>(cancellationToken);
            if (events is null || events.Count == 0)
                return [];

            return events
                .Select(e => e.Date)
                .Distinct()
                .OrderBy(d => d)
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
            logger.LogWarning(
                "Error al consultar el calendario de partidos en Estructura para la categoría {CategoryId}: {ErrorType}.",
                categoryId,
                ex.GetType().Name);
            return [];
        }
    }
}
