using Application.DTOs.Alerts;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace API.BackgroundServices;

/// <summary>
/// Background Worker nocturno que corre a las 03:00 AM (hora de Argentina) analizando los últimos 7 días
/// de check-ins de cada atleta para detectar patrones de alarma y fatiga acumulada (#177 · KAN-472).
/// </summary>
public sealed class DailyOverloadAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DailyOverloadAnalysisWorker> logger) : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);
    private DateOnly? _lastExecutionDate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Iniciando Background Worker diario de sobrecarga y avisos preventivos (KAN-472 · #177).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var targetHour = configuration.GetValue("Worker:DailyOverloadAnalysis:Hour", 3);
                var currentHour = ArgentinaTime.Now.Hour;
                var today = ArgentinaTime.Today;

                if (currentHour == targetHour && _lastExecutionDate != today)
                {
                    logger.LogInformation(
                        "Ejecutando análisis nocturno programado de las {TargetHour}:00 AM (Hora actual Argentina: {CurrentHour}h).",
                        targetHour,
                        currentHour);

                    await ProcessDailyAnalysisAsync(today, stoppingToken);
                    _lastExecutionDate = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Ocurrió un error inesperado durante el ciclo del worker nocturno de sobrecarga.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        logger.LogInformation("Background Worker diario de sobrecarga y avisos preventivos detenido.");
    }

    /// <summary>
    /// Ejecuta el proceso de análisis diario de sobrecarga. Expuesto públicamente para permitir
    /// testing unitario y de integración determinístico sin esperar el reloj de las 03:00 AM.
    /// </summary>
    public async Task<ProcessDailyOverloadAnalysisResult> ProcessDailyAnalysisAsync(
        DateOnly? targetDate = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProcessDailyOverloadAnalysisCommandHandler>();

        logger.LogInformation("Invocando handler de análisis diario de sobrecarga.");
        var result = await handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate), cancellationToken);

        logger.LogInformation(
            "Análisis nocturno finalizado con éxito. Atletas: {Athletes}, Alertas generadas: {Generated}, Omitidas: {Skipped}.",
            result.ProcessedAthletes,
            result.GeneratedAlerts,
            result.SkippedAlerts);

        return result;
    }
}

