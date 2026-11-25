using System.Net.Mail;
using Application.Interfaces.Services;

namespace Infrastructure.Services;

/// <summary>
/// Limitador en memoria con ventana fija de 15 minutos (máximo 3 intentos por IP y 3 por email normalizado) (KAN-575).
/// Thread-safe y con limpieza automática de ventanas expiradas para acotar el uso de memoria.
/// </summary>
public sealed class ForgotPasswordRateLimiter(TimeProvider timeProvider) : IForgotPasswordRateLimiter
{
    public const int MaxRequestsPerWindow = 3;
    public static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(15);
    private const int CleanupThreshold = 1024;
    private const int MaxTrackedKeys = 10_000;

    private sealed class WindowCounter
    {
        public DateTimeOffset WindowStart { get; set; }
        public int Count { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, WindowCounter> _counters = new(StringComparer.Ordinal);

    public ForgotPasswordRateLimitDecision TryAcquire(string? clientIp, string? email)
    {
        var now = timeProvider.GetUtcNow();
        var ipKey = $"ip:{(string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim())}";
        var normalizedEmail = NormalizeEmail(email);
        var emailKey = string.IsNullOrEmpty(normalizedEmail) ? null : $"email:{normalizedEmail}";

        lock (_sync)
        {
            if (_counters.Count >= CleanupThreshold)
            {
                CleanupExpired(now);
            }

            if (TryGetRetryAfter(ipKey, now, out var ipRetryAfter))
            {
                return new ForgotPasswordRateLimitDecision(false, ipRetryAfter);
            }

            if (emailKey is not null && TryGetRetryAfter(emailKey, now, out var emailRetryAfter))
            {
                return new ForgotPasswordRateLimitDecision(false, emailRetryAfter);
            }

            if (_counters.Count >= MaxTrackedKeys)
            {
                CleanupExpired(now);
                if (_counters.Count >= MaxTrackedKeys)
                {
                    var oldestKey = _counters.OrderBy(kv => kv.Value.WindowStart).First().Key;
                    _counters.Remove(oldestKey);
                }
            }

            Increment(ipKey, now);
            if (emailKey is not null)
            {
                Increment(emailKey, now);
            }

            return new ForgotPasswordRateLimitDecision(true);
        }
    }

    private bool TryGetRetryAfter(string key, DateTimeOffset now, out TimeSpan retryAfter)
    {
        if (_counters.TryGetValue(key, out var counter))
        {
            var windowEnd = counter.WindowStart + WindowDuration;
            if (now < windowEnd && counter.Count >= MaxRequestsPerWindow)
            {
                retryAfter = windowEnd - now;
                return true;
            }
        }

        retryAfter = TimeSpan.Zero;
        return false;
    }

    private void Increment(string key, DateTimeOffset now)
    {
        if (!_counters.TryGetValue(key, out var counter) || now >= counter.WindowStart + WindowDuration)
        {
            _counters[key] = new WindowCounter
            {
                WindowStart = now,
                Count = 1
            };
            return;
        }

        counter.Count++;
    }

    private void CleanupExpired(DateTimeOffset now)
    {
        var expiredKeys = _counters
            .Where(kv => now >= kv.Value.WindowStart + WindowDuration)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _counters.Remove(key);
        }
    }

    private static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var trimmed = email.Trim();
        if (MailAddress.TryCreate(trimmed, out var parsed))
        {
            return parsed.Address.Trim().ToUpperInvariant();
        }

        return trimmed.ToUpperInvariant();
    }
}
