namespace Gateway.Middleware;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Middleware de perímetro de seguridad del API Gateway (#KAN-539).
/// Valida la firma criptográfica localmente, consulta/cachea en memoria el estado de sesión (60s absolutos),
/// aplica política Fail-Closed (HTTP 503) y bloquea rutas no permitidas ante MustChangePassword (HTTP 403).
/// </summary>
public sealed class PerimeterSecurityMiddleware(
    RequestDelegate next,
    TokenValidationParameters tokenValidationParameters,
    ISessionValidationClient sessionClient,
    IMemoryCache cache,
    ILogger<PerimeterSecurityMiddleware> logger)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 1. Bloqueo perimetral de rutas internas para clientes externos (#KAN-528 / KAN-539)
        if (path.Contains("/api/v1/internal/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/api/v1/internal", StringComparison.OrdinalIgnoreCase))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No encontrado.",
                detail: "Ruta no encontrada o no accesible públicamente."
            ).ExecuteAsync(context);
            return;
        }

        // 2. Permitir preflights CORS y endpoints públicos
        if (HttpMethods.IsOptions(context.Request.Method) ||
            path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/identidad/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/identidad/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/identidad/api/v1/auth/google", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/identidad/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/identidad/api/v1/auth/forgot-password", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/identidad/api/v1/auth/reset-password", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authHeaderStr = authHeader.ToString();
            if (authHeaderStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var rawToken = authHeaderStr["Bearer ".Length..].Trim();

                // A. Validación criptográfica local del JWT en cada petición
                ClaimsPrincipal principal;
                try
                {
                    var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                    principal = tokenHandler.ValidateToken(rawToken, tokenValidationParameters, out _);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Token JWT inválido o firma adulterada.");
                    await Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "No autenticado.",
                        detail: "El token JWT es inválido o su firma ha sido adulterada."
                    ).ExecuteAsync(context);
                    return;
                }

                var userIdClaim = principal.FindFirst("sub")?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var stampClaim = principal.FindFirst("security_stamp")?.Value ?? string.Empty;

                if (!Guid.TryParse(userIdClaim, out var userId))
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "No autenticado.",
                        detail: "Token sin identificador de usuario válido."
                    ).ExecuteAsync(context);
                    return;
                }

                // B. Consulta o resolución con caché en memoria (60s absolutos) y control de concurrencia
                var cacheKey = $"session:{userId}:{stampClaim}";
                if (!cache.TryGetValue(cacheKey, out CachedSessionState? cachedState))
                {
                    var keyLock = KeyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
                    await keyLock.WaitAsync(context.RequestAborted);
                    try
                    {
                        if (!cache.TryGetValue(cacheKey, out cachedState))
                        {
                            var validation = await sessionClient.ValidateSessionAsync(userId, stampClaim, context.RequestAborted);

                            // Fail-Closed: si Identidad no responde y no hay caché, retornar 503
                            if (!validation.IsSuccess)
                            {
                                await Results.Problem(
                                    statusCode: StatusCodes.Status503ServiceUnavailable,
                                    title: "Servicio no disponible.",
                                    detail: "No se pudo verificar la validez de la sesión de forma segura."
                                ).ExecuteAsync(context);
                                return;
                            }

                            cachedState = new CachedSessionState(validation.IsValid, validation.MustChangePassword);

                            // Expiración absoluta fija (no se renueva con cada petición)
                            cache.Set(cacheKey, cachedState, new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = CacheDuration
                            });
                        }
                    }
                    finally
                    {
                        keyLock.Release();
                        if (keyLock.CurrentCount == 1)
                        {
                            KeyLocks.TryRemove(cacheKey, out _);
                        }
                    }
                }

                if (cachedState is null)
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Servicio no disponible.",
                        detail: "No se pudo verificar la validez de la sesión de forma segura."
                    ).ExecuteAsync(context);
                    return;
                }

                // C. Si la sesión fue revocada (SecurityStamp no coincide o usuario bloqueado)
                if (!cachedState.IsValid)
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Sesión invalidada.",
                        detail: "La sesión ha sido revocada por un cambio de credenciales."
                    ).ExecuteAsync(context);
                    return;
                }

                // D. Bloqueo 403 para usuarios con cambio de contraseña obligatorio
                if (cachedState.MustChangePassword)
                {
                    var isAllowedForPasswordChange =
                        path.Equals("/identidad/api/v1/auth/change-password", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/identidad/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/identidad/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/identidad/api/v1/auth/forgot-password", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/identidad/api/v1/auth/reset-password", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/identidad/health", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/health", StringComparison.OrdinalIgnoreCase);

                    if (!isAllowedForPasswordChange)
                    {
                        await Results.Problem(
                            statusCode: StatusCodes.Status403Forbidden,
                            title: "Cambio de contraseña obligatorio.",
                            detail: "Debe cambiar su contraseña antes de acceder a la aplicación."
                        ).ExecuteAsync(context);
                        return;
                    }
                }
            }
        }

        await next(context);
    }

    private sealed record CachedSessionState(bool IsValid, bool MustChangePassword);
}

