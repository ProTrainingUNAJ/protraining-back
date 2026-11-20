using System.Globalization;
using Application.Common.Helpers;
using Application.DTOs.Recommendations;

namespace Application.Common.Recommendations;

/// <summary>
/// Señal accionable detectada determinísticamente a partir del perfil frente al grupo y las tendencias (#196 · KAN-491).
/// </summary>
public sealed record RecommendationSignal(
    string Reason,
    string SuggestedFocus,
    IReadOnlyList<ProfileMetricComparisonDto> Metrics,
    WeeklyTrendsSummaryDto? Trends);

/// <summary>
/// Resultado consolidado del análisis determinístico del perfil del atleta (#196 · KAN-491).
/// </summary>
public sealed record ProfileAnalysisResult(
    IReadOnlyList<RecommendationSignal> Signals,
    IReadOnlyList<ProfileMetricComparisonDto> EvaluatedMetrics,
    WeeklyTrendsSummaryDto? TrendsSummary);

/// <summary>
/// Analizador determinístico de perfil físico frente al promedio del grupo y tendencias semanales (#196 · KAN-491).
/// Todos los cálculos numéricos se resuelven por reglas; la IA únicamente redacta sobre estos datos.
/// </summary>
public static class AthleteProfileAnalyzer
{
    public const double BelowAverageThresholdPercent = RecommendationsOptions.DefaultBelowAverageThresholdPercent;
    public const double AboveAverageThresholdPercent = 10.0;
    public const double LowWellnessAbsoluteThreshold = 3.0;
    public const double WellnessDropThreshold = -0.5;
    public const double WellnessDropCeiling = 3.5;
    public const double LoadSpikeThresholdPercent = 25.0;

    private sealed record PhysicalGapCandidate(
        string Reason,
        string SuggestedFocus,
        ProfileMetricComparisonDto Metric,
        double PercentageDifference);

    public static ProfileAnalysisResult Analyze(
        IReadOnlyList<AthleteFitnessTestRemoteDto> athleteTests,
        CategoryAveragesRemoteDto? categoryAverages,
        AthleteWeeklyTrendsRemoteDto? weeklyTrends,
        double belowAverageThresholdPercent = BelowAverageThresholdPercent)
    {
        var evaluatedMetrics = new List<ProfileMetricComparisonDto>();
        var physicalGaps = new List<PhysicalGapCandidate>();

        var effectiveBelowThreshold = RecommendationsOptions.NormalizeBelowAverageThreshold(belowAverageThresholdPercent);

        EvaluateGroupProfile(athleteTests, categoryAverages, evaluatedMetrics, physicalGaps, effectiveBelowThreshold);

        var (trendsSummary, hasNegativeWellness, hasLoadSpikeWithDrop) = AnalyzeTrends(weeklyTrends);
        var signals = BuildSignals(physicalGaps, trendsSummary, hasNegativeWellness, hasLoadSpikeWithDrop);

        return new ProfileAnalysisResult(signals, evaluatedMetrics, trendsSummary);
    }

    private static void EvaluateGroupProfile(
        IReadOnlyList<AthleteFitnessTestRemoteDto> athleteTests,
        CategoryAveragesRemoteDto? categoryAverages,
        List<ProfileMetricComparisonDto> evaluatedMetrics,
        List<PhysicalGapCandidate> physicalGaps,
        double belowAverageThresholdPercent)
    {
        if (athleteTests.Count == 0 || categoryAverages?.FitnessTests is not { Count: > 0 })
            return;

        var latestByTest = athleteTests
            .Where(t => t.Value >= 0 && !string.IsNullOrWhiteSpace(t.TestName) && !string.IsNullOrWhiteSpace(t.MeasurementUnit))
            .GroupBy(t => (
                TestName: TextNormalizer.Normalize(t.TestName),
                Unit: TextNormalizer.Normalize(t.MeasurementUnit)))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.RecordedAt).ThenByDescending(t => t.Attempt).First());

        if (latestByTest.Count == 0)
            return;

        var groupAveragesByTest = categoryAverages.FitnessTests
            .Where(ft => ft.TestedAthleteCount >= 2 && ft.Average > 0 && !string.IsNullOrWhiteSpace(ft.TestName) && !string.IsNullOrWhiteSpace(ft.Unit))
            .GroupBy(ft => (
                TestName: TextNormalizer.Normalize(ft.TestName),
                Unit: TextNormalizer.Normalize(ft.Unit)))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (key, latest) in latestByTest.OrderBy(entry => entry.Key.TestName, StringComparer.Ordinal))
        {
            if (!groupAveragesByTest.TryGetValue(key, out var groupAvgItem))
                continue;

            var athleteValue = (double)latest.Value;
            var groupAverage = groupAvgItem.Average;
            var lowerIsBetter = IsLowerBetter(latest.MeasurementUnit, latest.TestName);

            var pctDiff = lowerIsBetter
                ? Math.Round(((groupAverage - athleteValue) / groupAverage) * 100.0, 1, MidpointRounding.AwayFromZero)
                : Math.Round(((athleteValue - groupAverage) / groupAverage) * 100.0, 1, MidpointRounding.AwayFromZero);

            var aboveAverageThresholdPercent = Math.Abs(belowAverageThresholdPercent);

            var trendLabel = pctDiff < belowAverageThresholdPercent
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "Por debajo del promedio ({0:+0.0;-0.0;0.0}%)",
                    pctDiff)
                : pctDiff > aboveAverageThresholdPercent
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        "Superior al promedio ({0:+0.0;-0.0;0.0}%)",
                        pctDiff)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "En el promedio ({0:+0.0;-0.0;0.0}%)",
                        pctDiff);

            var unit = latest.MeasurementUnit.Trim();
            var metric = new ProfileMetricComparisonDto(
                Dimension: $"{latest.TestName.Trim()} ({unit})",
                AthleteValue: athleteValue,
                GroupAverage: groupAverage,
                Unit: unit,
                PercentageDifference: pctDiff,
                Trend: trendLabel);

            evaluatedMetrics.Add(metric);

            if (pctDiff < belowAverageThresholdPercent)
            {
                var (reason, focus) = MapGapReasonAndFocus(latest.TestName, latest.MotorCapacity);
                physicalGaps.Add(new PhysicalGapCandidate(reason, focus, metric, pctDiff));
            }
        }
    }

    private static (WeeklyTrendsSummaryDto? Summary, bool HasNegativeWellness, bool HasLoadSpikeWithDrop) AnalyzeTrends(
        AthleteWeeklyTrendsRemoteDto? weeklyTrends)
    {
        if (weeklyTrends?.Weeks is not { Count: > 0 })
            return (null, false, false);

        var orderedWeeks = weeklyTrends.Weeks
            .OrderBy(w => w.WeekStartDate)
            .ToList();

        var weeksEvaluated = orderedWeeks.Count(w => w.CheckInCount > 0 || w.LoadCount > 0);
        if (weeksEvaluated == 0)
            return (null, false, false);

        var wellnessPoints = orderedWeeks
            .Where(w => w.AverageWellness.HasValue && w.CheckInCount > 0)
            .ToList();

        double? latestWellness = null;
        double? previousWellness = null;
        double? wellnessDelta = null;

        if (wellnessPoints.Count > 0)
        {
            latestWellness = Math.Round(wellnessPoints[^1].AverageWellness!.Value, 2, MidpointRounding.AwayFromZero);
            if (wellnessPoints.Count >= 2)
            {
                previousWellness = Math.Round(
                    wellnessPoints.Take(wellnessPoints.Count - 1).Average(w => w.AverageWellness!.Value),
                    2,
                    MidpointRounding.AwayFromZero);
                wellnessDelta = Math.Round(latestWellness.Value - previousWellness.Value, 2, MidpointRounding.AwayFromZero);
            }
        }

        var loadPoints = orderedWeeks
            .Where(w => w.TotalLoad.HasValue && w.LoadCount > 0)
            .ToList();

        int? latestTotalLoad = null;
        double? previousAverageLoad = null;
        double? loadChangePercent = null;

        if (loadPoints.Count > 0)
        {
            latestTotalLoad = loadPoints[^1].TotalLoad!.Value;
            if (loadPoints.Count >= 2)
            {
                previousAverageLoad = Math.Round(
                    loadPoints.Take(loadPoints.Count - 1).Average(w => (double)w.TotalLoad!.Value),
                    1,
                    MidpointRounding.AwayFromZero);
                if (previousAverageLoad.Value > 0)
                {
                    loadChangePercent = Math.Round(
                        ((latestTotalLoad.Value - previousAverageLoad.Value) / previousAverageLoad.Value) * 100.0,
                        1,
                        MidpointRounding.AwayFromZero);
                }
            }
        }

        var hasNegativeWellness = latestWellness.HasValue &&
            (latestWellness.Value < LowWellnessAbsoluteThreshold ||
             (wellnessDelta.HasValue && wellnessDelta.Value <= WellnessDropThreshold && latestWellness.Value < WellnessDropCeiling));

        var hasLoadSpikeWithDrop = hasNegativeWellness &&
            loadChangePercent.HasValue &&
            loadChangePercent.Value >= LoadSpikeThresholdPercent;

        var trendLabel = hasLoadSpikeWithDrop
            ? "Sobrecarga creciente con caída de bienestar"
            : hasNegativeWellness
                ? "Caída en el bienestar semanal"
                : wellnessDelta >= 0.3
                    ? "Bienestar en alza"
                    : "Tendencia estable";

        var summary = new WeeklyTrendsSummaryDto(
            WeeksEvaluated: weeksEvaluated,
            LatestWellness: latestWellness,
            PreviousWellness: previousWellness,
            WellnessDelta: wellnessDelta,
            LatestTotalLoad: latestTotalLoad,
            PreviousAverageLoad: previousAverageLoad,
            LoadChangePercent: loadChangePercent,
            TrendLabel: trendLabel);

        return (summary, hasNegativeWellness, hasLoadSpikeWithDrop);
    }

    private static List<RecommendationSignal> BuildSignals(
        List<PhysicalGapCandidate> physicalGaps,
        WeeklyTrendsSummaryDto? trendsSummary,
        bool hasNegativeWellness,
        bool hasLoadSpikeWithDrop)
    {
        var signals = new List<RecommendationSignal>();

        string? trendContextSuffix = null;
        if (trendsSummary is not null)
        {
            if (hasLoadSpikeWithDrop && trendsSummary.LoadChangePercent.HasValue)
            {
                trendContextSuffix = string.Format(
                    CultureInfo.InvariantCulture,
                    " Dosificar el volumen semanal ya que las tendencias muestran aumento de carga (+{0:0.0}%) con caída de bienestar ({1:0.##}/5).",
                    trendsSummary.LoadChangePercent.Value,
                    trendsSummary.LatestWellness);
            }
            else if (hasNegativeWellness && trendsSummary.LatestWellness.HasValue)
            {
                trendContextSuffix = string.Format(
                    CultureInfo.InvariantCulture,
                    " Considerar una progresión conservadora por la caída reciente en el bienestar semanal ({0:0.##}/5).",
                    trendsSummary.LatestWellness.Value);
            }
        }

        var groupedGaps = physicalGaps
            .OrderBy(g => g.PercentageDifference)
            .GroupBy(g => TextNormalizer.Normalize(g.Reason), StringComparer.Ordinal);

        foreach (var group in groupedGaps)
        {
            var primary = group.First();
            var focus = trendContextSuffix is not null
                ? $"{primary.SuggestedFocus}.{trendContextSuffix}"
                : primary.SuggestedFocus;

            signals.Add(new RecommendationSignal(
                Reason: primary.Reason,
                SuggestedFocus: focus,
                Metrics: group.Select(g => g.Metric).ToList(),
                Trends: trendsSummary));
        }

        if (hasNegativeWellness && trendsSummary is not null)
        {
            if (hasLoadSpikeWithDrop)
            {
                var details = string.Format(
                    CultureInfo.InvariantCulture,
                    "evaluar una descarga o ajuste de volumen semanal antes de incrementar intensidad, priorizando recuperación (carga semanal +{0:0.0}% con bienestar en {1:0.##}/5)",
                    trendsSummary.LoadChangePercent!.Value,
                    trendsSummary.LatestWellness!.Value);

                signals.Add(new RecommendationSignal(
                    Reason: "aumento de carga con caída en el bienestar semanal",
                    SuggestedFocus: details,
                    Metrics: [],
                    Trends: trendsSummary));
            }
            else
            {
                var baselineText = trendsSummary.PreviousWellness.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, " frente a {0:0.##}/5 previo", trendsSummary.PreviousWellness.Value)
                    : string.Empty;

                var details = string.Format(
                    CultureInfo.InvariantCulture,
                    "monitorear fatiga y descanso del atleta y ajustar la densidad o volumen semanal (bienestar actual en {0:0.##}/5{1})",
                    trendsSummary.LatestWellness!.Value,
                    baselineText);

                signals.Add(new RecommendationSignal(
                    Reason: "tendencia a la baja en bienestar semanal",
                    SuggestedFocus: details,
                    Metrics: [],
                    Trends: trendsSummary));
            }
        }

        return signals;
    }

    private static readonly string[] HigherTimeIsBetterKeywords =
    [
        "plancha",
        "plank",
        "isometric",
        "sosten",
        "aguante",
        "suspension",
        "wall sit",
        "dead hang",
        "colgado",
        "equilibrio",
        "balance",
        "cooper",
        "yo-yo",
        "yoyo",
        "course navette",
        "beep"
    ];

    private static readonly string[] LowerTimeIsBetterKeywords =
    [
        "sprint",
        "carrera",
        "agility",
        "agilidad",
        "cambios de direccion",
        "illinois",
        "t-test",
        "5-10-5",
        "bronco",
        "10 m",
        "20 m",
        "30 m",
        "40 m",
        "50 m",
        "100 m",
        "1 km",
        "1000 m",
        "1500 m",
        "2000 m",
        "milla",
        "reaccion",
        "tiempo"
    ];

    public static bool IsLowerBetter(string unit, string testName)
    {
        var normUnit = TextNormalizer.Normalize(unit);
        var normName = TextNormalizer.Normalize(testName);

        // Pruebas isométricas o de sostén/resistencia por tiempo (ej. plancha): más tiempo es mejor
        if (HigherTimeIsBetterKeywords.Any(normName.Contains))
            return false;

        if (LowerTimeIsBetterKeywords.Any(normName.Contains))
            return true;

        // En milisegundos y segundos (que no sean sostén isométrico), menor tiempo indica mejor marca
        if (normUnit is "ms" or "s" or "seg" or "segundos")
            return true;

        // En minutos solo es "menos es mejor" si el nombre indicó explícitamente una prueba de distancia/tiempo
        // (ya evaluada arriba); así una plancha o prueba de resistencia en minutos no queda invertida
        return normName.Contains("velocidad") && normUnit.Contains('s');
    }

    private static (string Reason, string SuggestedFocus) MapGapReasonAndFocus(string testName, string motorCapacity)
    {
        var normName = TextNormalizer.Normalize(testName);
        var normCapacity = TextNormalizer.Normalize(motorCapacity);

        if (normName.Contains("salto") || normName.Contains("cmj") || normName.Contains("abalakov") || normName.Contains("squat jump"))
        {
            return (
                "salto por debajo del promedio",
                "incorporar 2 bloques semanales de pliometría progresiva de bajo a moderado impacto y fuerza explosiva de tren inferior");
        }

        if (normName.Contains("sprint") || normName.Contains("carrera 30") || normName.Contains("carrera 100") ||
            normName.Contains("velocidad") || normCapacity is "velocity" or "velocidad")
        {
            return (
                "velocidad por debajo del promedio",
                "incluir sprints cortos con recuperación completa, técnica de aceleración y fuerza específica de empuje horizontal");
        }

        if (normName.Contains("sentadilla") || normName.Contains("press") || normName.Contains("dominada") ||
            normName.Contains("fuerza") || normCapacity is "strength" or "fuerza")
        {
            return (
                "fuerza por debajo del promedio",
                "reforzar el bloque principal de fuerza básica con progresión gradual de cargas y control técnico");
        }

        if (normName.Contains("cooper") || normName.Contains("bronco") || normName.Contains("1 km") ||
            normName.Contains("resistencia") || normCapacity is "endurance" or "resistencia")
        {
            return (
                "resistencia por debajo del promedio",
                "sumar trabajo intermitente aeróbico progresivo y control de densidad de pausas en la semana");
        }

        if (normName.Contains("agility") || normName.Contains("cambios de direccion") ||
            normName.Contains("agilidad") || normCapacity is "agility" or "agilidad")
        {
            return (
                "agilidad por debajo del promedio",
                "trabajar desaceleraciones, cambios de dirección reactivos y estabilidad de tobillo y cadera");
        }

        if (normName.Contains("sit and reach") || normName.Contains("flexibilidad") || normCapacity is "flexibility" or "flexibilidad")
        {
            return (
                "flexibilidad por debajo del promedio",
                "agregar bloques diarios de movilidad articular y elongación específica en entrada en calor y vuelta a la calma");
        }

        if (normName.Contains("coordinacion") || normName.Contains("balance") || normCapacity is "coordination" or "coordinacion")
        {
            return (
                "coordinación por debajo del promedio",
                "incluir ejercicios unilaterales de control motor, propiocepción y coordinación neuromuscular en activación");
        }

        return (
            $"{testName.Trim().ToLowerInvariant()} por debajo del promedio",
            "reforzar esta capacidad con bloques específicos progresivos en la planificación semanal");
    }
}
