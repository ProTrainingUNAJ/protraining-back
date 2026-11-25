using Application.Common.Helpers;
using Application.DTOs.Recommendations;
using Domain.Entities;

namespace Application.Common.Recommendations;

/// <summary>
/// Política determinística de deduplicación y reincidencia tras descarte para el motor de recomendaciones (KAN-491):
/// - Si ya existe una recomendación Pending o Approved vigente para ese atleta y motivo, no se duplica.
/// - Si la recomendación fue descartada por el PF, no vuelve a proponerse hasta acumular al menos
///   <c>discardedToleranceMeasurements</c> mediciones nuevas de ese test posteriores al descarte;
///   al alcanzarse ese margen y continuar por debajo del umbral, se vuelve a proponer avisando al PF.
/// </summary>
public static class RecommendationRecurrencePolicy
{
    public static readonly RecommendationStatus[] DeduplicationStatuses =
    [
        RecommendationStatus.Pending,
        RecommendationStatus.Approved,
        RecommendationStatus.Discarded
    ];

    public static IReadOnlyList<RecommendationSignal> FilterEligibleSignals(
        IReadOnlyList<RecommendationSignal> signals,
        IReadOnlyList<AthleteFitnessTestRemoteDto> athleteTests,
        IReadOnlyList<AthleteRecommendation> existingRecommendations,
        int discardedToleranceMeasurements = RecommendationsOptions.DefaultDiscardedToleranceMeasurements)
    {
        if (signals.Count == 0)
            return [];

        var effectiveTolerance = RecommendationsOptions.NormalizeDiscardedToleranceMeasurements(discardedToleranceMeasurements);

        var trainingExistingByReason = existingRecommendations
            .Where(r => r.Type == RecommendationType.Training)
            .GroupBy(r => TextNormalizer.Normalize(r.Reason), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var eligible = new List<RecommendationSignal>(signals.Count);
        var seenReasons = new HashSet<string>(StringComparer.Ordinal);

        foreach (var signal in signals)
        {
            var normalizedReason = TextNormalizer.Normalize(signal.Reason);
            if (!seenReasons.Add(normalizedReason))
                continue;

            if (!trainingExistingByReason.TryGetValue(normalizedReason, out var history) || history.Count == 0)
            {
                eligible.Add(signal);
                continue;
            }

            // Pendientes o aprobadas vigentes bloquean duplicados
            if (history.Any(r => r.Status is RecommendationStatus.Pending or RecommendationStatus.Approved))
                continue;

            var lastDiscarded = history
                .Where(r => r.Status == RecommendationStatus.Discarded)
                .OrderByDescending(r => r.ReviewedAtUtc ?? r.UpdatedAt)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault();

            if (lastDiscarded is null)
            {
                eligible.Add(signal);
                continue;
            }

            // Para señales de tendencias sin tests físicos asociados, la descartada permanece bloqueada
            if (signal.SourceTests is not { Count: > 0 })
                continue;

            var discardedAtUtc = lastDiscarded.ReviewedAtUtc ?? lastDiscarded.UpdatedAt;
            var qualifyingMatch = FindTestMeetingTolerance(
                signal.SourceTests,
                athleteTests,
                discardedAtUtc,
                effectiveTolerance);

            if (qualifyingMatch is null)
                continue;

            eligible.Add(signal with
            {
                IsRecurringAfterDiscard = true,
                RecurringTestName = qualifyingMatch.Value.TestName,
                NewMeasurementsSinceDiscard = qualifyingMatch.Value.NewMeasurementCount
            });
        }

        return eligible;
    }

    /// <summary>
    /// Construye el aviso breve de reincidencia para el PF dependiente del test físico evaluado
    /// (ej.: "Sigue por debajo en salto vertical: reforzar potencia de piernas").
    /// </summary>
    public static string BuildRecurringReminder(string? testName, string reason)
    {
        var displayTest = ResolveDisplayTestName(testName, reason);
        var actionHint = MapRecurringActionHint(testName, reason);
        return $"Sigue por debajo en {displayTest}: {actionHint}";
    }

    private static (string TestName, int NewMeasurementCount)? FindTestMeetingTolerance(
        IReadOnlyList<RecommendationTestKey> sourceTests,
        IReadOnlyList<AthleteFitnessTestRemoteDto> athleteTests,
        DateTime discardedAtUtc,
        int toleranceMeasurements)
    {
        if (athleteTests.Count == 0)
            return null;

        var discardedUtc = ToUtc(discardedAtUtc);

        foreach (var sourceTest in sourceTests)
        {
            var targetName = TextNormalizer.Normalize(sourceTest.TestName);
            var targetUnit = TextNormalizer.Normalize(sourceTest.MeasurementUnit);

            var newCount = athleteTests.Count(t =>
                t.Value >= 0 &&
                ToUtc(t.RecordedAt) > discardedUtc &&
                TextNormalizer.Normalize(t.TestName) == targetName &&
                TextNormalizer.Normalize(t.MeasurementUnit) == targetUnit);

            if (newCount >= toleranceMeasurements)
            {
                return (sourceTest.TestName, newCount);
            }
        }

        return null;
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string ResolveDisplayTestName(string? testName, string reason)
    {
        if (!string.IsNullOrWhiteSpace(testName))
            return testName.Trim().ToLowerInvariant();

        var trimmedReason = reason.Trim().ToLowerInvariant();
        const string suffix = " por debajo del promedio";
        if (trimmedReason.EndsWith(suffix, StringComparison.Ordinal))
        {
            var extracted = trimmedReason[..^suffix.Length].Trim();
            if (!string.IsNullOrEmpty(extracted))
                return extracted;
        }

        return "esta evaluación";
    }

    private static string MapRecurringActionHint(string? testName, string reason)
    {
        var combined = TextNormalizer.Normalize($"{testName} {reason}");

        if (combined.Contains("salto") || combined.Contains("cmj") || combined.Contains("abalakov") || combined.Contains("squat jump"))
            return "reforzar potencia de piernas";

        if (combined.Contains("sprint") || combined.Contains("carrera") || combined.Contains("velocidad"))
            return "reforzar aceleración y velocidad lineal";

        if (combined.Contains("plancha") || combined.Contains("plank") || combined.Contains("core") || combined.Contains("isometric"))
            return "reforzar estabilidad y fuerza de zona media";

        if (combined.Contains("sentadilla") || combined.Contains("press") || combined.Contains("dominada") || combined.Contains("fuerza"))
            return "reforzar fuerza básica con progresión controlada";

        if (combined.Contains("cooper") || combined.Contains("bronco") || combined.Contains("1 km") || combined.Contains("resistencia"))
            return "reforzar resistencia aeróbica e intermitente";

        if (combined.Contains("agility") || combined.Contains("cambios de direccion") || combined.Contains("agilidad"))
            return "reforzar agilidad y cambios de dirección";

        if (combined.Contains("sit and reach") || combined.Contains("flexibilidad"))
            return "reforzar movilidad articular y flexibilidad";

        if (combined.Contains("coordinacion") || combined.Contains("balance") || combined.Contains("equilibrio"))
            return "reforzar coordinación y control motor";

        return "reforzar esta capacidad con bloques específicos";
    }
}
