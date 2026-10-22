using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace Infrastructure.Services;

public interface IWebPushClientWrapper
{
    Task SendNotificationAsync(WebPush.PushSubscription subscription, string payload, VapidDetails vapidDetails, CancellationToken cancellationToken = default);
}

public sealed class DefaultWebPushClientWrapper : IWebPushClientWrapper
{
    private readonly WebPushClient _client = new();

    public Task SendNotificationAsync(WebPush.PushSubscription subscription, string payload, VapidDetails vapidDetails, CancellationToken cancellationToken = default) =>
        _client.SendNotificationAsync(subscription, payload, vapidDetails, cancellationToken);
}

public sealed class PushNotificationService(
    IPushSubscriptionRepository subscriptionRepository,
    IUnitOfWork unitOfWork,
    IOptions<VapidOptions> vapidOptions,
    IWebPushClientWrapper webPushClient,
    ILogger<PushNotificationService> logger) : IPushNotificationService
{
    public async Task SendNotificationAsync(Guid athleteId, string title, string message, CancellationToken cancellationToken = default)
    {
        var subscriptions = await subscriptionRepository.GetActiveByAthleteIdAsync(athleteId, cancellationToken);

        if (subscriptions.Count == 0)
        {
            logger.LogInformation("No se encontraron suscripciones activas para el atleta {AthleteId}.", athleteId);
            return;
        }

        var vapid = vapidOptions.Value;
        if (!vapid.IsConfigured)
        {
            logger.LogWarning("VAPID no está configurado en el entorno. Se omite el envío Web Push real para el atleta {AthleteId}.", athleteId);
            return;
        }

        var vapidDetails = new VapidDetails(vapid.Subject, vapid.PublicKey, vapid.PrivateKey);
        var payloadJson = JsonSerializer.Serialize(new
        {
            title,
            body = message,
            url = "/athlete/check-in",
            tag = "wellness-reminder"
        }, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await webPushClient.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails, cancellationToken);

                sub.RecordDelivery(DateTime.UtcNow);
                await subscriptionRepository.UpdateAsync(sub, cancellationToken);

                logger.LogInformation("Notificación Web Push enviada con éxito al dispositivo del atleta {AthleteId}.", athleteId);
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // La suscripción expiró o fue desinstalada en el dispositivo cliente; se desactiva
                logger.LogInformation("Suscripción Web Push expirada/inválida ({StatusCode}) para el atleta {AthleteId}. Desactivando.", ex.StatusCode, athleteId);
                sub.Deactivate();
                await subscriptionRepository.UpdateAsync(sub, cancellationToken);
            }
            catch (WebPushException ex)
            {
                logger.LogWarning(ex, "Fallo al enviar notificación Web Push al atleta {AthleteId}. Código HTTP: {StatusCode}", athleteId, ex.StatusCode);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error inesperado al despachar notificación Web Push para el atleta {AthleteId}.", athleteId);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
