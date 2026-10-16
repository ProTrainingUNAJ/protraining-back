using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class PushNotificationService(ILogger<PushNotificationService> logger) : IPushNotificationService
{
    public Task SendNotificationAsync(Guid athleteId, string title, string message, CancellationToken cancellationToken = default)
    {
        // registra el envio de la notificacion push al atleta
        logger.LogInformation(
            "Enviando notificacion push al atleta {AthleteId}. Titulo: {Title}. Mensaje: {Message}",
            athleteId,
            title,
            message);

        return Task.CompletedTask;
    }
}
