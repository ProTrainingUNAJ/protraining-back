using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProTraining.Application.Common;
using ProTraining.Domain.Entities;

namespace ProTraining.Infrastructure.Analysis;

public class AnalysisProvider : IProveedorDeAnalisis
{
    private readonly ILogger<AnalysisProvider> _logger;
    private readonly string? _apiKey;

    public AnalysisProvider(IConfiguration configuration, ILogger<AnalysisProvider> logger)
    {
        _logger = logger;
        _apiKey = configuration["AiAnalysis:ApiKey"];
    }

    public async Task<AnalysisResult?> AnalizarSemanaAtletaAsync(
        Athlete atleta,
        List<DailyCheckIn> checkIns,
        List<Injury> lesionesActivas,
        CancellationToken ct = default)
    {
        if (checkIns.Count == 0 && lesionesActivas.Count == 0)
            return null;

        // Si hay una API Key configurada para IA, intentaríamos invocarla aquí.
        // Como regla de diseño (PDF Sección 6 / Anexo), el sistema conmuta a reglas locales
        // deterministas si no hay clave configurada o si falla la conexión externa.
        return EvaluateLocalRules(atleta, checkIns, lesionesActivas);
    }

    private AnalysisResult? EvaluateLocalRules(Athlete atleta, List<DailyCheckIn> checkIns, List<Injury> lesionesActivas)
    {
        var recentCheckIns = checkIns.OrderByDescending(c => c.Date).Take(7).ToList();

        // Regla 1: Lesión activa combinada con check-ins de molestia o esfuerzo
        if (lesionesActivas.Any())
        {
            var lesion = lesionesActivas.First();
            return new AnalysisResult
            {
                RequiresAlert = true,
                Severity = "Alta",
                Reason = $"El atleta tiene una lesión activa en {lesion.BodyZone}. Requiere supervisión médica antes de continuar con cargas pesadas.",
                Suggestion = "Revisar la rutina del día, ajustar porcentajes o sustituir ejercicios que involucren la zona afectada."
            };
        }

        // Regla 2: Esfuerzo alto sostenido (3 o más sesiones con RPE >= 8) y poco sueño (< 5h)
        int highEffortCount = recentCheckIns.Count(c => c.EffortRate >= 8);
        bool lowSleep = recentCheckIns.Any(c => c.SleepHours > 0 && c.SleepHours < 5.0m);

        if (highEffortCount >= 3 && lowSleep)
        {
            return new AnalysisResult
            {
                RequiresAlert = true,
                Severity = "Alta",
                Reason = "Sobrecarga acumulada: 3 sesiones consecutivas con esfuerzo >= 8 y registro de descanso menor a 5 horas.",
                Suggestion = "Programar una sesión de descarga o movilidad pasiva. Reducir volumen general un 30%."
            };
        }

        // Regla 3: Zonas de dolor repetidas en los check-ins de la semana
        var painZonesReported = recentCheckIns
            .Where(c => !string.IsNullOrWhiteSpace(c.PainZones))
            .SelectMany(c => c.PainZones.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(z => z.ToLowerInvariant())
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToList();

        if (painZonesReported.Any())
        {
            string zonas = string.Join(", ", painZonesReported);
            return new AnalysisResult
            {
                RequiresAlert = true,
                Severity = "Media",
                Reason = $"Molestia recurrente reportada en: {zonas} durante múltiples check-ins de la semana.",
                Suggestion = $"Consultar al atleta al inicio del entrenamiento. Evitar sobrecargar {zonas} hoy."
            };
        }

        // Regla 4: Ánimo bajo persistente con esfuerzo moderado-alto
        bool lowMood = recentCheckIns.Count(c => c.MoodRate <= 2) >= 3;
        if (lowMood && highEffortCount >= 2)
        {
            return new AnalysisResult
            {
                RequiresAlert = true,
                Severity = "Baja",
                Reason = "Fatiga psicológica y estado anímico bajo en las últimas sesiones.",
                Suggestion = "Verificar estado de recuperación general y hablar informalmente con el atleta."
            };
        }

        return null;
    }
}
