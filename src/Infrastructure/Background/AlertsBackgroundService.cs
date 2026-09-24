using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProTraining.Application.Common;
using ProTraining.Domain.Entities;
using ProTraining.Infrastructure.Data;

namespace ProTraining.Infrastructure.Background;

public class AlertsBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlertsBackgroundService> _logger;

    public AlertsBackgroundService(IServiceProvider serviceProvider, ILogger<AlertsBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AlertsBackgroundService de ProTraining iniciado.");

        // Ejecutar inmediatamente al inicio y luego cada 12 horas
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDailyAnalysisAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar análisis diario de avisos preventivos.");
            }

            // Esperar 12 horas o hasta que se cancele el token
            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }

    public async Task RunDailyAnalysisAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var analyzer = scope.ServiceProvider.GetRequiredService<IProveedorDeAnalisis>();

        // Desactivar filtros de tenant temporalmente para procesar todos los atletas del sistema
        var athletes = await db.Athletes
            .IgnoreQueryFilters()
            .Include(a => a.Injuries)
            .Include(a => a.CheckIns)
            .ToListAsync(ct);

        var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-7);

        foreach (var athlete in athletes)
        {
            var recentCheckIns = athlete.CheckIns
                .Where(c => c.Date >= sevenDaysAgo)
                .ToList();

            var activeInjuries = athlete.Injuries
                .Where(i => i.Status == InjuryStatus.Active || i.Status == InjuryStatus.InRecovery)
                .ToList();

            var result = await analyzer.AnalizarSemanaAtletaAsync(athlete, recentCheckIns, activeInjuries, ct);

            if (result != null && result.RequiresAlert)
            {
                // Evitar duplicar la misma alerta en el mismo día
                bool alertExistsToday = await db.PreventiveAlerts
                    .IgnoreQueryFilters()
                    .AnyAsync(a => a.AthleteId == athlete.Id && a.DateGenerated.Date == DateTime.UtcNow.Date && a.Reason == result.Reason, ct);

                if (!alertExistsToday)
                {
                    var newAlert = new PreventiveAlert
                    {
                        AccountId = athlete.AccountId,
                        AthleteId = athlete.Id,
                        DateGenerated = DateTime.UtcNow,
                        Severity = result.Severity,
                        Reason = result.Reason,
                        Suggestion = result.Suggestion,
                        IsReviewed = false
                    };

                    db.PreventiveAlerts.Add(newAlert);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
