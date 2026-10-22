using System.Threading;
using System.Threading.Tasks;
using Application.DTOs.Notifications;
using Application.UseCases.Notifications.RegisterPushSubscription;
using Application.UseCases.Notifications.UnsubscribePush;
using Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace API.Controllers;

public sealed record UnsubscribePushRequest(string Endpoint);

[ApiController]
[Route("api/v1/push-subscriptions")]
[Authorize]
public sealed class PushSubscriptionsController(
    RegisterPushSubscriptionCommandHandler registerHandler,
    UnsubscribePushCommandHandler unsubscribeHandler,
    IOptions<VapidOptions> vapidOptions) : ControllerBase
{
    [HttpGet("public-key")]
    public ActionResult<VapidPublicKeyDto> GetPublicKey()
    {
        var publicKey = vapidOptions.Value.PublicKey;
        return Ok(new VapidPublicKeyDto(publicKey));
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterPushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Keys is null ||
            string.IsNullOrWhiteSpace(request.Endpoint) ||
            string.IsNullOrWhiteSpace(request.Keys.P256dh) ||
            string.IsNullOrWhiteSpace(request.Keys.Auth))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Datos de suscripción inválidos",
                Detail = "El endpoint, la clave p256dh y la clave auth son obligatorios.",
                Status = 400
            });
        }

        var command = new RegisterPushSubscriptionCommand(
            request.Endpoint,
            request.Keys.P256dh,
            request.Keys.Auth,
            request.UserAgent);

        await registerHandler.HandleAsync(command, cancellationToken);

        return Ok(new { message = "Suscripción Web Push registrada con éxito." });
    }

    [HttpDelete]
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(
        [FromQuery] string? endpoint,
        [FromBody] UnsubscribePushRequest? request,
        CancellationToken cancellationToken)
    {
        var targetEndpoint = !string.IsNullOrWhiteSpace(endpoint)
            ? endpoint
            : request?.Endpoint;

        if (string.IsNullOrWhiteSpace(targetEndpoint))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Datos inválidos",
                Detail = "El endpoint de suscripción a revocar es obligatorio.",
                Status = 400
            });
        }

        await unsubscribeHandler.HandleAsync(new UnsubscribePushCommand(targetEndpoint), cancellationToken);

        return NoContent();
    }
}
