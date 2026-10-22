using System;
using System.Threading;
using System.Threading.Tasks;
using Application.UseCases.Reminders.SendDailyWellnessReminders;
using Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace API.BackgroundServices;

public sealed class WellnessReminderWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
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
        var windowStart = configuration.GetValue("Reminders:WindowStart", 16);
        var windowEnd = configuration.GetValue("Reminders:WindowEnd", 20);
        var currentHour = ArgentinaTime.Now.Hour;

        // solo procesa por la tarde dentro de la ventana horaria configurada (ej. de 16 a 20 h de Argentina)
        if (currentHour < windowStart || currentHour >= windowEnd)
        {
            logger.LogInformation(
                "Fuera de la ventana horaria de recordatorios de la tarde (hora Argentina: {CurrentHour}h, ventana: {WindowStart}h-{WindowEnd}h). Omitiendo ejecucion.",
                currentHour,
                windowStart,
                windowEnd);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SendDailyWellnessRemindersCommandHandler>();

        logger.LogInformation("Ejecutando proceso de recordatorios de wellness en horario de la tarde.");
        var result = await handler.HandleAsync(new SendDailyWellnessRemindersCommand(), cancellationToken);

        logger.LogInformation(
            "Proceso de recordatorios finalizado. Enviados: {Sent}, Omitidos ya enviados: {SkippedAlreadySent}, Omitidos ya con check-in: {SkippedCheckedIn}",
            result.RemindersSent,
            result.SkippedAlreadySent,
            result.SkippedAlreadyCheckedIn);
    }
}
