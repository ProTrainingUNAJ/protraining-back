namespace Application.Interfaces.Services;

public sealed record ForgotPasswordRateLimitDecision(bool IsAllowed, TimeSpan? RetryAfter = null);

/// <summary>
/// Controla los límites independientes por IP y por email normalizado para POST /api/v1/auth/forgot-password (KAN-575).
/// </summary>
public interface IForgotPasswordRateLimiter
{
    ForgotPasswordRateLimitDecision TryAcquire(string? clientIp, string? email);
}
