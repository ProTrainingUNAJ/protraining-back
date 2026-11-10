namespace API.Middleware;

using System.IdentityModel.Tokens.Jwt;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// Middleware que restringe el acceso para sesiones invalidadas (SecurityStamp) y para usuarios con cambio obligatorio de contraseña.
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<User> userManager)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var stampClaim = context.User.FindFirst("security_stamp")?.Value;

            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "No autenticado.",
                        detail: "La cuenta de usuario no existe, está inactiva o bloqueada."
                    ).ExecuteAsync(context);
                    return;
                }

                // 1. Si el sello de seguridad del token no coincide con el de la base de datos (PostgreSQL), la sesión fue revocada
                if (!string.IsNullOrEmpty(stampClaim) && !string.Equals(user.SecurityStamp, stampClaim, StringComparison.Ordinal))
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Sesión invalidada.",
                        detail: "La sesión ha sido revocada por un cambio de credenciales."
                    ).ExecuteAsync(context);
                    return;
                }

                // 2. Si el usuario debe cambiar su contraseña obligatoriamente (en BD o en el token), restringir a endpoints de cambio/salida
                if (user.MustChangePassword || context.User.HasClaim("must_change_password", "true"))
                {
                    var path = context.Request.Path.Value ?? string.Empty;

                    var isAllowed =
                        path.Equals("/api/v1/auth/change-password", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/health", StringComparison.OrdinalIgnoreCase);

                    if (!isAllowed)
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
}
