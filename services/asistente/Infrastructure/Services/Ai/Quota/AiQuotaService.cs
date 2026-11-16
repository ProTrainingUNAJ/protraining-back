namespace Infrastructure.Services.Ai.Quota;

using System.Collections.Concurrent;
using Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Implementación en memoria del limitador de cuota diaria de IA por preparador físico (#215 · KAN-510).
/// Gestiona contadores indexados por PF y fecha UTC con reseteo automático al cambiar el día.
/// </summary>
public sealed class AiQuotaService : IAiQuotaService
{
    private readonly IMemoryCache? _cache;
    private readonly ConcurrentDictionary<string, int> _fallbackCounters = new();
    private readonly AiQuotaOptions _options;
    private readonly ILogger<AiQuotaService> _logger;
    private readonly object _syncLock = new();

    public AiQuotaService(
        IOptions<AiQuotaOptions> options,
        ILogger<AiQuotaService> logger,
        IMemoryCache? cache = null)
    {
        _options = options.Value;
        _logger = logger;
        _cache = cache;
    }

    public int DailyLimit => _options.DailyLimitPerPf;

    private static string BuildKey(Guid pfId, DateOnly date) => $"ai_quota_{pfId}_{date:yyyyMMdd}";

    public bool HasQuota(Guid? pfId)
    {
        if (!_options.Enabled || !pfId.HasValue || pfId.Value == Guid.Empty)
            return true;

        var usage = GetCurrentUsage(pfId);
        return usage < _options.DailyLimitPerPf;
    }

    public int GetCurrentUsage(Guid? pfId)
    {
        if (!pfId.HasValue || pfId.Value == Guid.Empty)
            return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = BuildKey(pfId.Value, today);

        if (_cache != null && _cache.TryGetValue<int>(key, out var cachedUsage))
        {
            return cachedUsage;
        }

        return _fallbackCounters.TryGetValue(key, out var fallbackUsage) ? fallbackUsage : 0;
    }

    public int RecordUsage(Guid? pfId)
    {
        if (!pfId.HasValue || pfId.Value == Guid.Empty)
            return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = BuildKey(pfId.Value, today);

        lock (_syncLock)
        {
            var current = GetCurrentUsage(pfId);
            var next = current + 1;

            if (_cache != null)
            {
                // Expira tras 25 horas para limpiar memoria pasada la jornada UTC
                _cache.Set(key, next, TimeSpan.FromHours(25));
            }

            _fallbackCounters[key] = next;

            _logger.LogInformation(
                "Consumo de IA registrado para el preparador físico {PfId}: {Usage}/{Limit} pedidos consumidos hoy.",
                pfId.Value, next, _options.DailyLimitPerPf);

            if (next >= _options.DailyLimitPerPf)
            {
                _logger.LogWarning(
                    "El preparador físico {PfId} ha alcanzado el límite diario de IA ({Usage}/{Limit}). Las siguientes consultas se derivarán a reglas determinísticas.",
                    pfId.Value, next, _options.DailyLimitPerPf);
            }

            return next;
        }
    }
}
