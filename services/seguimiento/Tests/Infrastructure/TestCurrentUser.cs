using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;

namespace Seguimiento.Tests.Infrastructure;

// Usuario de prueba configurable: simula el claim "pf_id" del JWT y el rol del usuario.
// Sin pfId se comporta como el arranque del servicio (sin token), que es
// exactamente el contexto en el que corre el CatalogTestSeeder.
public sealed class TestCurrentUser(
    Guid? pfId = null,
    IHttpContextAccessor? httpContextAccessor = null,
    string? role = null) : ICurrentUser
{
    public Guid? PfId =>
        (httpContextAccessor?.HttpContext?.User.FindFirst("pf_id")?.Value is { } val && Guid.TryParse(val, out var parsed))
            ? parsed
            : pfId;

    public bool IsInRole(string targetRole)
    {
        if (role is not null)
            return string.Equals(role, targetRole, StringComparison.OrdinalIgnoreCase);

        var user = httpContextAccessor?.HttpContext?.User;
        if (user is null)
            return false;

        return user.IsInRole(targetRole) || user.HasClaim("role", targetRole);
    }
}
