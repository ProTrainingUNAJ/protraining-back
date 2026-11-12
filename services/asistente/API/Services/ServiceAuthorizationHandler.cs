using System.Net.Http.Headers;
using Application.Interfaces.Services;

namespace API.Services;

/// <summary>
/// Manda siempre un token con rol 'Servicio', aunque haya un usuario en la petición (KAN-578).
/// Para los clientes que llaman a endpoints internos (/api/v1/internal/...), que no aceptan el token del PF.
/// </summary>
public sealed class ServiceAuthorizationHandler(IServiceTokenFactory tokenFactory) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenFactory.CreateToken());
        return base.SendAsync(request, cancellationToken);
    }
}
