using System;
using System.Threading;
using System.Threading.Tasks;
using Application.UseCases.Athletes.PurgePendingAthletes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace API.BackgroundServices;

public sealed class DataPurgeWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DataPurgeWorker> logger) : BackgroundService
{
    private readonly TimeSpan _defaultCheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Iniciando Worker de purga de datos con reintentos (KAN-498).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPurgesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ocurrió un error inesperado al ejecutar el proceso de purga de datos.");
            }

            var intervalHours = configuration.GetValue("Purge:IntervalHours", 24);
            var delay = intervalHours > 0 ? TimeSpan.FromHours(intervalHours) : _defaultCheckInterval;
            await Task.Delay(delay, stoppingToken);
        }

        logger.LogInformation("Worker de purga de datos detenido.");
    }

    public async Task<ProcessPendingPurgesResult> ProcessPurgesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProcessPendingPurgesCommandHandler>();
        var retentionDays = configuration.GetValue("Purge:RetentionDays", ProcessPendingPurgesCommandHandler.DefaultRetentionDays);

        logger.LogInformation("Ejecutando ciclo de purga de datos de atletas en segundo plano (Retención: {RetentionDays} días).", retentionDays);
        var result = await handler.HandleAsync(new ProcessPendingPurgesCommand(retentionDays), cancellationToken);

        logger.LogInformation(
            "Ciclo de purga finalizado. Evaluados: {Evaluated}, Purgados: {Purged}, Pospuestos por error/reintento: {Failed}, Omitidos por retención: {Skipped}.",
            result.TotalEvaluated,
            result.PurgedSuccessfully,
            result.FailedOrPostponed,
            result.SkippedWithinRetention);

        return result;
    }
}
