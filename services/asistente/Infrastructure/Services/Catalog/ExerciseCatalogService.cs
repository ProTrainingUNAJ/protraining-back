namespace Infrastructure.Services.Catalog;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

/// <summary>
/// Cliente HTTP hacia el microservicio de Entrenamiento para consultar y verificar
/// los ejercicios en la biblioteca oficial. Reenvía la autorización del preparador físico
/// a través de ForwardAuthorizationHandler y utiliza IMemoryCache para persistir el catálogo en memoria.
/// </summary>
public sealed class ExerciseCatalogService : IExerciseCatalogService
{
    public const string ExercisesPath = "api/v1/exercises";
    private const string CacheKeyPrefixId = "cat_ex_id_";
    private const string CacheKeyPrefixName = "cat_ex_name_";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ExerciseCatalogService> _logger;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "con", "en", "el", "la", "los", "las", "un", "una", "unos", "unas", "para", "por", "al", "del", "y", "o", "no"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ExerciseCatalogService(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        ICurrentUser currentUser,
        ILogger<ExerciseCatalogService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CatalogExerciseDto?> FindByIdAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        if (exerciseId == Guid.Empty)
        {
            return Task.FromResult<CatalogExerciseDto?>(null);
        }

        // Búsqueda en memoria local; se eliminó la llamada con ?ids= que no existe en el contrato oficial (KAN-530)
        return Task.FromResult(GetCachedById(exerciseId));
    }

    public async Task<CatalogExerciseDto?> FindByNameAsync(string exerciseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exerciseName))
        {
            return null;
        }

        var normalized = NormalizeText(exerciseName);

        // 1. Coincidencia en caché en memoria
        var cached = GetCachedByName(normalized);
        if (cached != null)
        {
            return cached;
        }

        var withoutParentheses = RemoveParentheses(normalized);
        if (!string.IsNullOrEmpty(withoutParentheses))
        {
            cached = GetCachedByName(withoutParentheses);
            if (cached != null)
            {
                return cached;
            }
        }

        // 2. Consulta HTTP de búsqueda hacia Entrenamiento (búsqueda por trigramas)
        var searchTerm = !string.IsNullOrEmpty(withoutParentheses) ? withoutParentheses : normalized;
        var candidates = await SearchAsync(searchTerm, cancellationToken);
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

        // 4. Coincidencia por similitud estricta de términos clave (F1 score sobre tokens significativos)
        var searchMeaningful = withoutParentheses
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !StopWords.Contains(t) && t.Length > 2)
            .ToList();

        if (searchMeaningful.Count == 0)
        {
            // Sin coincidencia exacta ni tokens distintivos, no se asume coincidencia (Ajuste N2: va como referencia)
            return null;
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

            // Se exige tanto recall alto como precisión para no sustituir por ejercicios distintos (caso Enzo: Burpees vs Salto al cajón)
            if (recall >= 0.70 && precision >= 0.60 && f1 >= 0.65 && f1 > highestScore)
            {
                highestScore = f1;
                bestMatch = item;
            }
        }

        // Si no hay una coincidencia con puntaje suficiente, devuelve null (no sustituye por el más parecido)
        return bestMatch;
    }

    public async Task<IReadOnlyList<CatalogExerciseDto>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(searchTerm)
                ? $"{ExercisesPath}?pageSize=50"
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

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseExercisesFromJson(json);
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

    private IReadOnlyList<CatalogExerciseDto> ParseExercisesFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        List<RemoteExerciseDto>? items = null;

        // Intento 1: Deserializar según contrato oficial PaginatedExerciseResponse (con propiedad "items")
        try
        {
            var paginated = JsonSerializer.Deserialize<PaginatedExerciseResponseDto>(json, JsonOptions);
            if (paginated?.Items != null)
            {
                items = paginated.Items;
            }
        }
        catch (JsonException)
        {
            // Fallback transitorio si Entrenamiento devuelve una lista plana
        }

        // Intento 2: Lista plana directa de ejercicios
        if (items == null)
        {
            try
            {
                items = JsonSerializer.Deserialize<List<RemoteExerciseDto>>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "No se pudo deserializar la respuesta de ejercicios de Entrenamiento.");
                return [];
            }
        }

        if (items == null || items.Count == 0)
        {
            return [];
        }

        var result = new List<CatalogExerciseDto>(items.Count);
        foreach (var item in items)
        {
            var dto = new CatalogExerciseDto(item.Id, item.Name, item.MuscleGroup, item.Equipment ?? string.Empty);
            CacheItem(dto);
            result.Add(dto);
        }

        return result;
    }

    private string GetPfIdKey() => _currentUser.PfId?.ToString() ?? "global";
    private string BuildIdKey(Guid id) => $"{CacheKeyPrefixId}{GetPfIdKey()}_{id}";
    private string BuildNameKey(string normalizedName) => $"{CacheKeyPrefixName}{GetPfIdKey()}_{normalizedName}";

    private void CacheItem(CatalogExerciseDto dto)
    {
        var entryOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl };
        _memoryCache.Set(BuildIdKey(dto.Id), dto, entryOptions);

        var norm = NormalizeText(dto.Name);
        if (!string.IsNullOrEmpty(norm))
        {
            _memoryCache.Set(BuildNameKey(norm), dto, entryOptions);
        }

        var withoutParens = RemoveParentheses(norm);
        if (!string.IsNullOrEmpty(withoutParens) && withoutParens != norm)
        {
            _memoryCache.Set(BuildNameKey(withoutParens), dto, entryOptions);
        }
    }

    private CatalogExerciseDto? GetCachedById(Guid id)
    {
        return _memoryCache.TryGetValue(BuildIdKey(id), out CatalogExerciseDto? cached) ? cached : null;
    }

    private CatalogExerciseDto? GetCachedByName(string normalizedName)
    {
        return _memoryCache.TryGetValue(BuildNameKey(normalizedName), out CatalogExerciseDto? cached) ? cached : null;
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

    private sealed class PaginatedExerciseResponseDto
    {
        [JsonPropertyName("items")]
        public List<RemoteExerciseDto>? Items { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
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
