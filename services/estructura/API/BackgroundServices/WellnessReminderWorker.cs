using System;
using System.Threading;
using System.Threading.Tasks;
using Application.UseCases.Reminders.SendDailyWellnessReminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace API.BackgroundServices;

public sealed class WellnessReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WellnessReminderWorker> logger) : BackgroundService
{
    // intervalo de ejecucion: verifica periodicamente en segundo plano
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Iniciando Worker de recordatorios diarios de wellness (KAN-369).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ocurrio un error inesperado al procesar recordatorios de wellness.");
            }

            // espera el intervalo configurado antes de volver a verificar
            await Task.Delay(_checkInterval, stoppingToken);
        }

        logger.LogInformation("Worker de recordatorios diarios de wellness detenido.");
    }

    public async Task ProcessRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SendDailyWellnessRemindersCommandHandler>();

        logger.LogInformation("Ejecutando proceso de recordatorios de wellness.");
        var result = await handler.HandleAsync(new SendDailyWellnessRemindersCommand(), cancellationToken);

        logger.LogInformation(
            "Proceso de recordatorios finalizado. Enviados: {Sent}, Omitidos ya enviados: {SkippedAlreadySent}, Omitidos ya con check-in: {SkippedCheckedIn}",
            result.RemindersSent,
            result.SkippedAlreadySent,
            result.SkippedAlreadyCheckedIn);
    }
}
