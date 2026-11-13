using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace API.Services;

/// <summary>
/// Inyecta credenciales en llamadas salientes hacia otros microservicios
/// </summary>
public sealed class ServiceAuthenticationHandler(
    IServiceTokenFactory tokenFactory,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. Si existe token de usuario en la petición actual, se reenvía
        var userAuth = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(userAuth) && request.Headers.Authorization is null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", userAuth);
        }
        else if (request.Headers.Authorization is null)
        {
            // 2. Si no hay usuario (ej. background worker), se genera y adjunta un JWT con rol "Servicio"
            var token = tokenFactory.CreateToken();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
