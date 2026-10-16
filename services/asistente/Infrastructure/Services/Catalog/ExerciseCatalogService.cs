namespace Infrastructure.Services.Catalog;

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Cliente HTTP hacia el microservicio de Entrenamiento para consultar y verificar
/// los ejercicios en la biblioteca oficial. Reenvía la autorización del preparador físico
/// a través de ForwardAuthorizationHandler y no depende de lecturas locales de disco ni listas estáticas.
/// </summary>
public sealed class ExerciseCatalogService : IExerciseCatalogService
{
    public const string ExercisesPath = "api/v1/exercises";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ExerciseCatalogService> _logger;
    private readonly ConcurrentDictionary<Guid, CatalogExerciseDto> _cacheById = new();
    private readonly ConcurrentDictionary<string, CatalogExerciseDto> _cacheByName = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "con", "en", "el", "la", "los", "las", "un", "una", "unos", "unas", "para", "por", "al", "del", "y", "o", "no"
    };

    public ExerciseCatalogService(HttpClient httpClient, ILogger<ExerciseCatalogService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CatalogExerciseDto?> FindByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        if (exerciseId == Guid.Empty)
        {
            return null;
        }

        // 1. Verificación en caché local de respuestas previas
        if (_cacheById.TryGetValue(exerciseId, out var cached))
        {
            return cached;
        }

        // 2. Si la caché local está vacía, realizamos una carga inicial del catálogo
        if (_cacheById.IsEmpty)
        {
            await GetAllAsync(cancellationToken);
            if (_cacheById.TryGetValue(exerciseId, out cached))
            {
                return cached;
            }
        }

        // 3. Consulta HTTP directa al microservicio de Entrenamiento
        try
        {
            using var response = await _httpClient.GetAsync($"{ExercisesPath}/{exerciseId}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var item = await response.Content.ReadFromJsonAsync<RemoteExerciseDto>(cancellationToken: cancellationToken);
                if (item != null)
                {
                    var dto = new CatalogExerciseDto(item.Id, item.Name, item.MuscleGroup, item.Equipment ?? string.Empty);
                    _cacheById[item.Id] = dto;
                    _cacheByName[NormalizeText(item.Name)] = dto;
                    return dto;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo obtener el ejercicio {Id} desde el endpoint individual de Entrenamiento.", exerciseId);
        }

        return null;
    }

    public async Task<CatalogExerciseDto?> FindByNameAsync(string exerciseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exerciseName))
        {
            return null;
        }

        var normalized = NormalizeText(exerciseName);

        // 1. Coincidencia en caché local
        if (_cacheByName.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var withoutParentheses = RemoveParentheses(normalized);
        if (_cacheByName.TryGetValue(withoutParentheses, out var cleanCached))
        {
            return cleanCached;
        }

        // 2. Consulta HTTP de búsqueda hacia Entrenamiento
        var candidates = await SearchAsync(withoutParentheses, cancellationToken);
        if (candidates.Count == 0 && withoutParentheses != normalized)
        {
            candidates = await SearchAsync(exerciseName, cancellationToken);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // 3. Coincidencia exacta por nombre normalizado
        var exactMatch = candidates.FirstOrDefault(c =>
            NormalizeText(c.Name) == normalized ||
            NormalizeText(c.Name) == withoutParentheses ||
            RemoveParentheses(NormalizeText(c.Name)) == withoutParentheses);

        if (exactMatch != null)
        {
            return exactMatch;
        }

        // 4. Coincidencia por similitud de términos clave (F1 score sobre tokens significativos)
        var searchMeaningful = withoutParentheses
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !StopWords.Contains(t) && t.Length > 2)
            .ToList();

        if (searchMeaningful.Count == 0)
        {
            return candidates.FirstOrDefault();
        }

        CatalogExerciseDto? bestMatch = null;
        var highestScore = 0.0;

        foreach (var item in candidates)
        {
            var itemNorm = RemoveParentheses(NormalizeText(item.Name));
            var itemMeaningful = itemNorm
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !StopWords.Contains(t) && t.Length > 2)
                .ToList();

            if (itemMeaningful.Count == 0) continue;

            var common = searchMeaningful.Count(st => itemMeaningful.Any(it => it.Equals(st, StringComparison.OrdinalIgnoreCase)));
            if (common == 0) continue;

            var recall = (double)common / searchMeaningful.Count;
            var precision = (double)common / itemMeaningful.Count;
            var f1 = 2 * (precision * recall) / (precision + recall);

            if (recall >= 0.70 && f1 > highestScore)
            {
                highestScore = f1;
                bestMatch = item;
            }
        }

        return bestMatch ?? candidates.FirstOrDefault();
    }

    public async Task<IReadOnlyList<CatalogExerciseDto>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(searchTerm)
                ? $"{ExercisesPath}?pageSize=100"
                : $"{ExercisesPath}?search={Uri.EscapeDataString(searchTerm)}&pageSize=50";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Entrenamiento respondió {Status} al buscar ejercicios por término '{Search}'.", (int)response.StatusCode, searchTerm);
                return [];
            }

            var items = await response.Content.ReadFromJsonAsync<List<RemoteExerciseDto>>(cancellationToken: cancellationToken);
            if (items == null || items.Count == 0)
            {
                return [];
            }

            var result = new List<CatalogExerciseDto>(items.Count);
            foreach (var item in items)
            {
                var dto = new CatalogExerciseDto(item.Id, item.Name, item.MuscleGroup, item.Equipment ?? string.Empty);
                _cacheById[item.Id] = dto;
                _cacheByName[NormalizeText(item.Name)] = dto;
                result.Add(dto);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar los ejercicios en el microservicio de Entrenamiento.");
            return [];
        }
    }

    public async Task<IReadOnlyList<CatalogExerciseDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await SearchAsync(string.Empty, cancellationToken);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToLowerInvariant();
    }

    private static string RemoveParentheses(string text)
    {
        var sb = new StringBuilder();
        var inside = false;
        foreach (var ch in text)
        {
            if (ch == '(') inside = true;
            else if (ch == ')') inside = false;
            else if (!inside) sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private sealed class RemoteExerciseDto
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("muscleGroup")]
        public string MuscleGroup { get; set; } = string.Empty;

        [JsonPropertyName("equipment")]
        public string? Equipment { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
