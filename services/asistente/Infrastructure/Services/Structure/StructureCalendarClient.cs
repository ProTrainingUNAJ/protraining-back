using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.Services;
using Application.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Structure;

/// <summary>
/// Cliente HTTP resiliente para consultar los partidos de una categoría en Estructura (KAN-406 / KAN-408 / KAN-473 / KAN-486).
/// Utiliza el endpoint interno con rol 'Servicio' (#178 · KAN-473).
/// Incorpora degradación elegante a memoria caché ante caídas o indisponibilidad de Estructura (#191 · KAN-486).
/// No silencia errores de autenticación o autorización (401/403).
/// </summary>
public sealed class StructureCalendarClient(
    HttpClient httpClient,
    ILogger<StructureCalendarClient> logger,
    IMemoryCache? memoryCache = null) : IStructureCalendarClient
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(60);

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
        if (categoryId == Guid.Empty)
            return [];

        var cacheKey = $"struct_cal_{categoryId}_{from:yyyyMMdd}_{to:yyyyMMdd}";

        if (httpClient.BaseAddress is null)
        {
            if (TryGetCachedFallback(cacheKey, categoryId, "La dirección base de Estructura no está configurada", out var cached))
                return cached;

            logger.LogError("La dirección base de Estructura no está configurada.");
            throw new StructureServiceUnavailableException(
                "La dirección base del microservicio de Estructura no está configurada.");
        }

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
                if (TryGetCachedFallback(cacheKey, categoryId, $"Estructura respondió código {(int)response.StatusCode}", out var cached))
                    return cached;

                logger.LogError(
                    "No se pudo obtener el calendario de la categoría {CategoryId}. Código HTTP {StatusCode}.",
                    categoryId,
                    (int)response.StatusCode);
                throw new StructureServiceUnavailableException(
                    $"Estructura no pudo consultar el calendario (código {(int)response.StatusCode}).");
            }

            var events = await response.Content.ReadFromJsonAsync<List<CategoryCalendarEventDto>>(cancellationToken);
            var result = events is null || events.Count == 0
                ? (IReadOnlyList<DateOnly>)[]
                : events.Select(e => e.Date).Distinct().OrderBy(d => d).ToList();

            memoryCache?.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });

            return result;
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
            if (TryGetCachedFallback(cacheKey, categoryId, "Error de conectividad al consultar Estructura", out var cached, ex))
                return cached;

            logger.LogError(
                ex,
                "Error al consultar el calendario de partidos en Estructura para la categoría {CategoryId}.",
                categoryId);
            throw new StructureServiceUnavailableException(
                "El microservicio de Estructura no se encuentra disponible.", ex);
        }
    }

    private bool TryGetCachedFallback(
        string cacheKey,
        Guid categoryId,
        string reason,
        out IReadOnlyList<DateOnly> cachedDates,
        Exception? exception = null)
    {
        if (memoryCache != null && memoryCache.TryGetValue(cacheKey, out IReadOnlyList<DateOnly>? cached) && cached != null)
        {
            if (exception != null)
            {
                logger.LogWarning(
                    exception,
                    "{Reason} para la categoría {CategoryId}. Degradando elegantemente a la caché de fixture ({Count} fechas).",
                    reason, categoryId, cached.Count);
            }
            else
            {
                logger.LogWarning(
                    "{Reason} para la categoría {CategoryId}. Degradando elegantemente a la caché de fixture ({Count} fechas).",
                    reason, categoryId, cached.Count);
            }

            cachedDates = cached;
            return true;
        }

        cachedDates = [];
        return false;
    }
}
