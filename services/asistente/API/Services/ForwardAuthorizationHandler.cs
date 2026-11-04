using System.Net.Http.Headers;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;

namespace API.Services;

/// <summary>
/// Reenvía el token de la petición actual a los llamados a otros servicios (Entrenamiento, Seguimiento, Estructura).
/// Si no hay contexto HTTP de usuario activo (ej. ejecución de Background Worker nocturno), genera
/// y adjunta un token con rol 'Servicio' (#177 · KAN-472).
/// </summary>
public sealed class ForwardAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    IServiceTokenFactory? tokenFactory = null) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization) && request.Headers.Authorization is null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        else if (request.Headers.Authorization is null && tokenFactory is not null)
        {
            var serviceToken = tokenFactory.CreateToken();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
