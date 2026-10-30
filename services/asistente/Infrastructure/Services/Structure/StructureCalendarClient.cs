using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP resiliente para consultar los partidos de una categoría en Estructura (KAN-406 / KAN-408 / KAN-410).
/// Si Estructura no está disponible o no hay eventos configurados, devuelve vacío sin interrumpir el flujo.
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
            var url = $"api/v1/categories/{categoryId}/events?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
            using var response = await httpClient.GetAsync(url, cancellationToken);

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
