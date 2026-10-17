namespace Infrastructure.Services.Ai.Cache;

using System.Security.Cryptography;
using System.Text;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Implementación de caché en memoria para respuestas de IA utilizando IMemoryCache (KAN-368).
/// Almacena y recupera propuestas estructuradas identificadas por el hash SHA-256 de sus instrucciones,
/// evitando llamadas redundantes a Ollama, Gemini o al motor de reglas.
/// </summary>
public sealed class AiResponseMemoryCache(
    IMemoryCache memoryCache,
    IOptions<AiCacheOptions> options,
    ILogger<AiResponseMemoryCache> logger) : IAiResponseCache
{
    private readonly AiCacheOptions _options = options.Value;

    public bool TryGet(string promptHash, out RoutineSuggestionResponseDto? cachedResponse)
    {
        cachedResponse = null;

        if (!_options.Enabled || string.IsNullOrWhiteSpace(promptHash))
        {
            return false;
        }

        if (memoryCache.TryGetValue(promptHash, out RoutineSuggestionResponseDto? entry) && entry != null)
        {
            logger.LogInformation("Caché HIT: Respuesta de IA encontrada en memoria para el hash '{Hash}'.", promptHash);
            cachedResponse = entry;
            return true;
        }

        logger.LogDebug("Caché MISS: No se encontró respuesta en memoria para el hash '{Hash}'.", promptHash);
        return false;
    }

    public void Set(string promptHash, RoutineSuggestionResponseDto response, TimeSpan? customTtl = null)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(promptHash) || response == null)
        {
            return;
        }

        var ttl = customTtl ?? TimeSpan.FromMinutes(Math.Max(1, _options.TtlMinutes));
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        memoryCache.Set(promptHash, response, entryOptions);
        logger.LogInformation("Caché SET: Almacenada respuesta de IA para el hash '{Hash}' con vencimiento de {TtlMinutes} minutos.", promptHash, ttl.TotalMinutes);
    }

    public string ComputePromptHash(string goal, IEnumerable<string>? muscleGroupEmphasis, Guid? pfId = null)
    {
        var normalizedGoal = (goal ?? string.Empty).Trim().ToLowerInvariant();

        var normalizedMuscles = muscleGroupEmphasis is not null
            ? string.Join(",", muscleGroupEmphasis
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Distinct()
                .OrderBy(m => m, StringComparer.Ordinal))
            : "general";

        if (string.IsNullOrEmpty(normalizedMuscles))
        {
            normalizedMuscles = "general";
        }

        var pfPrefix = pfId.HasValue && pfId.Value != Guid.Empty ? $"pf:{pfId.Value}|" : string.Empty;
        var canonicalInstructions = $"{pfPrefix}goal:{normalizedGoal}|muscles:{normalizedMuscles}";
        return ComputeSha256(canonicalInstructions);
    }

    public string ComputePromptHash(string promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return string.Empty;
        }

        return ComputeSha256(promptText.Trim());
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
