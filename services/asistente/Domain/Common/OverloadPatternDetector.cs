using Domain.Entities;

namespace Domain.Common;

/// <summary>
/// Registro simplificado de check-in para evaluación de patrones de sobrecarga y riesgo en el dominio.
/// </summary>
public sealed record AthleteCheckInRecord(
    DateOnly Date,
    double SleepHours,
    int SleepQuality,
    int MuscleSoreness,
    int FatigueLevel,
    int MoodRate,
    int StressLevel,
    int? EffortRate,
    bool IsSick,
    string? PainZones,
    bool HasActiveInjury);

/// <summary>
/// Resultado del análisis de patrones de sobrecarga sobre una ventana de check-ins (#177 · KAN-472).
/// </summary>
public sealed record OverloadPatternResult(
    bool HasPattern,
    AlertSeverity Severity,
    AlertType Type,
    string Reason,
    string Suggestion,
    SuggestedAdjustmentType? SuggestedAdjustmentType,
    int? SuggestedAdjustmentPercentage)
{
    public static OverloadPatternResult None => new(
        false,
        AlertSeverity.Low,
        AlertType.AccumulatedFatigue,
        string.Empty,
        string.Empty,
        null,
        null);
}

/// <summary>
/// Detector de patrones de sobrecarga, fatiga acumulada y riesgo de lesión en el dominio (#177 · KAN-472).
/// Evalúa el historial de check-ins del atleta (típicamente los últimos 7 días) mediante reglas determinísticas.
/// </summary>
public static class OverloadPatternDetector
{
    public static OverloadPatternResult Detect(Guid athleteId, IReadOnlyList<AthleteCheckInRecord> records)
    {
        if (athleteId == Guid.Empty || records.Count == 0)
        {
            return OverloadPatternResult.None;
        }

        // Ordenar por fecha cronológica
        var ordered = records.OrderBy(r => r.Date).ToList();

        // 1. Patrón Crítico: Lesión activa o dolor reportado persistente con esfuerzo medio/alto
        var injuryRecord = ordered.LastOrDefault(r => r.HasActiveInjury);
        if (injuryRecord is not null)
        {
            var painDescription = !string.IsNullOrWhiteSpace(injuryRecord.PainZones)
                ? $" en zona: {injuryRecord.PainZones.Trim()}"
                : string.Empty;

            return new OverloadPatternResult(
                HasPattern: true,
                Severity: AlertSeverity.High,
                Type: AlertType.InjuryRisk,
                Reason: $"Riesgo de lesión inminente: el atleta reporta lesión activa o molestia aguda{painDescription} en sus check-ins recientes.",
                Suggestion: "Suspender ejercicios que sobrecarguen la zona afectada, priorizar kinesiología o descanso preventivo.",
                SuggestedAdjustmentType: SuggestedAdjustmentType.Rest,
                SuggestedAdjustmentPercentage: 50);
        }

        // 2. Patrón de Alarma A: Esfuerzo alto combinado con déficit severo de sueño (RPE >= 8 + Sueño < 6h o Calidad <= 2)
        var highEffortLowSleepRecords = ordered
            .Where(r => r.EffortRate.HasValue && r.EffortRate.Value >= 8 && (r.SleepHours < 6.0 || r.SleepQuality <= 2))
            .ToList();

        if (highEffortLowSleepRecords.Count > 0)
        {
            var worst = highEffortLowSleepRecords.Last();
            return new OverloadPatternResult(
                HasPattern: true,
                Severity: AlertSeverity.High,
                Type: AlertType.AccumulatedFatigue,
                Reason: $"Patrón de alarma: sesión de esfuerzo elevado (RPE {worst.EffortRate}) combinada con descanso insuficiente ({worst.SleepHours:0.#} horas de sueño, calidad {worst.SleepQuality}/5).",
                Suggestion: "Ajustar la intensidad de la próxima sesión, reducir volumen general un 25% y priorizar protocolos de recuperación y sueño.",
                SuggestedAdjustmentType: SuggestedAdjustmentType.LoadReduction,
                SuggestedAdjustmentPercentage: 25);
        }

        // 3. Patrón de Alarma B: RPE alto sostenido (3 o más sesiones con RPE >= 8 en la semana)
        var highRpeCount = ordered.Count(r => r.EffortRate.HasValue && r.EffortRate.Value >= 8);
        if (highRpeCount >= 3)
        {
            return new OverloadPatternResult(
                HasPattern: true,
                Severity: AlertSeverity.High,
                Type: AlertType.AcuteOverload,
                Reason: $"Sobrecarga aguda sostenida: {highRpeCount} sesiones con RPE >= 8 en los últimos 7 días.",
                Suggestion: "Programar descarga activa o sesión regenerativa. Se sugiere una reducción de carga del 20%.",
                SuggestedAdjustmentType: SuggestedAdjustmentType.LoadReduction,
                SuggestedAdjustmentPercentage: 20);
        }

        // 4. Patrón de Fatiga Severa Acumulada: Promedio de fatiga >= 4.0 o 3+ días con fatiga >= 4 o dolor muscular >= 4
        var highFatigueDays = ordered.Count(r => r.FatigueLevel >= 4 || r.MuscleSoreness >= 4);
        var avgFatigue = ordered.Average(r => r.FatigueLevel);
        if (avgFatigue >= 4.0 || highFatigueDays >= 3)
        {
            return new OverloadPatternResult(
                HasPattern: true,
                Severity: AlertSeverity.Medium,
                Type: AlertType.AccumulatedFatigue,
                Reason: $"Fatiga muscular acumulada elevada en los últimos 7 días (promedio de fatiga: {avgFatigue:0.1}/5, {highFatigueDays} días con dolor o fatiga severa).",
                Suggestion: "Modificar la rutina hacia trabajo de movilidad, fuerza compensatoria y descarga progresiva.",
                SuggestedAdjustmentType: SuggestedAdjustmentType.RoutineAdjustment,
                SuggestedAdjustmentPercentage: 15);
        }

        return OverloadPatternResult.None;
    }
}

