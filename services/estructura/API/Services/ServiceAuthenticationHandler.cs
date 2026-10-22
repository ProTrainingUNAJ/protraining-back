using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace API.Services;

/// <summary>
/// inyecta credenciales en llamadas salientes hacia otros microservicios
/// </summary>
public sealed class ServiceAuthenticationHandler(
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. si existe token de usuario en la peticion actual, se reenvia
        var userAuth = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(userAuth) && request.Headers.Authorization is null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", userAuth);
        }

        // 2. si no hay usuario (ej. background worker), se inyecta la clave de servicio
        var serviceKey = configuration["Services:Auth:ServiceKey"];
        if (!string.IsNullOrWhiteSpace(serviceKey))
        {
            request.Headers.TryAddWithoutValidation("X-Service-Key", serviceKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
