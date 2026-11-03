using Application.DTOs.Benchmark;
using Application.Exceptions;
using Application.Interfaces.Services;

namespace Application.UseCases.Benchmark;

public sealed class GetAthleteGroupBenchmarkQueryHandler(
    IStructureClient structureClient,
    IFitnessTestMeasurementService fitnessTestMeasurementService)
{
    public async Task<GroupBenchmarkResponseDto> HandleAsync(
        GetAthleteGroupBenchmarkQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Validar identidad del atleta autenticado contra /me de Estructura (KAN-450)
        var me = await structureClient.GetMeAsync(cancellationToken);
        if (me is null)
        {
            throw new NoAthleteLinkedException();
        }

        // Si intenta consultar un ID ajeno, responder 403 Forbidden (comportamiento aprobado para KAN-449)
        if (me.AthleteId != query.AthleteId)
        {
            throw new ForbiddenException();
        }

        // 2. Resolver y validar la categoría solicitada
        Guid targetCategoryId;
        string targetCategoryName;

        if (query.CategoryId.HasValue)
        {
            var matchingCategory = me.Categories?.FirstOrDefault(c => c.Id == query.CategoryId.Value);
            if (matchingCategory is null)
            {
                throw new CategoryNotFoundException();
            }

            targetCategoryId = matchingCategory.Id;
            targetCategoryName = matchingCategory.Name;
        }
        else
        {
            var defaultCategory = me.Categories?.FirstOrDefault();
            if (defaultCategory is null)
            {
                return new GroupBenchmarkResponseDto(
                    query.AthleteId,
                    Guid.Empty,
                    string.Empty,
                    Array.Empty<GroupBenchmarkMetricDto>());
            }

            targetCategoryId = defaultCategory.Id;
            targetCategoryName = defaultCategory.Name;
        }

        // 3. Obtener los atletas activos de la categoría desde Estructura
        var categoryAthletes = await structureClient.GetCategoryAthletesAsync(targetCategoryId, cancellationToken);
        var athleteIds = categoryAthletes
            .Select(a => a.Id)
            .Distinct()
            .ToList();

        if (athleteIds.Count == 0)
        {
            return new GroupBenchmarkResponseDto(
                query.AthleteId,
                targetCategoryId,
                targetCategoryName,
                Array.Empty<GroupBenchmarkMetricDto>());
        }

        // 4. Obtener mediciones de tests físicos para todos los atletas de la categoría
        var allMeasurements = await fitnessTestMeasurementService.GetMeasurementsByAthletesAsync(
            athleteIds,
            cancellationToken);

        // Identificar los compañeros de la categoría (excluyendo estrictamente al atleta consultante)
        var peerIds = athleteIds.Where(id => id != query.AthleteId).ToHashSet();

        // Agrupar mediciones válidas por atleta para cada (TestType, Unit)
        var validMeasurements = allMeasurements
            .Where(m => athleteIds.Contains(m.AthleteId) && m.Value >= 0)
            .GroupBy(m => (TestType: m.TestType.Trim(), Unit: m.Unit.Trim().ToLowerInvariant()))
            .ToList();

        // Mapear por test: cuántos compañeros distintos tienen datos y el promedio de la categoría
        var categoryStats = new Dictionary<(string TestType, string Unit), (int PeerCount, double GroupAverage)>();

        foreach (var group in validMeasurements)
        {
            var latestPerAthlete = group
                .GroupBy(m => m.AthleteId)
                .ToDictionary(
                    ag => ag.Key,
                    ag => ag.OrderByDescending(m => m.Date).First().Value
                );

            var peerCount = latestPerAthlete.Keys.Count(id => peerIds.Contains(id));
            var categoryAvg = Math.Round(latestPerAthlete.Values.Average(), 1, MidpointRounding.AwayFromZero);

            categoryStats[group.Key] = (peerCount, categoryAvg);
        }

        // 5. Obtener mediciones del atleta autenticado
        var athleteMeasurements = allMeasurements
            .Where(m => m.AthleteId == query.AthleteId && m.Value >= 0)
            .GroupBy(m => (TestType: m.TestType.Trim(), Unit: m.Unit.Trim().ToLowerInvariant()))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.Date).First()
            );

        var metricsList = new List<GroupBenchmarkMetricDto>();

        foreach (var (key, athleteMeasurement) in athleteMeasurements.OrderBy(m => m.Key.TestType))
        {
            var athleteVal = athleteMeasurement.Value;
            var unit = athleteMeasurement.Unit;
            var direction = DetectDirection(unit, key.TestType);
            var dimension = $"{key.TestType} ({unit})";

            var hasStats = categoryStats.TryGetValue(key, out var stats);
            var peersWithData = hasStats ? stats.PeerCount : 0;

            // Regla mandatoria de k-anonimato: al menos 3 compañeros distintos con mediciones válidas
            if (peersWithData >= 3)
            {
                var groupAvg = stats.GroupAverage;
                var diff = Math.Round(athleteVal - groupAvg, 1, MidpointRounding.AwayFromZero);

                double pctDiff = 0.0;
                if (groupAvg > 0)
                {
                    if (direction == "LowerIsBetter")
                    {
                        pctDiff = Math.Round(((groupAvg - athleteVal) / groupAvg) * 100.0, 1, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        pctDiff = Math.Round(((athleteVal - groupAvg) / groupAvg) * 100.0, 1, MidpointRounding.AwayFromZero);
                    }
                }

                string status;
                string trend;

                if (pctDiff < -2.0)
                {
                    status = "BelowAverage";
                    trend = $"Por debajo del promedio ({pctDiff:+0.0;-0.0;0.0}%)";
                }
                else if (pctDiff > 2.0)
                {
                    status = "AboveAverage";
                    trend = $"Superior al promedio ({pctDiff:+0.0;-0.0;0.0}%)";
                }
                else
                {
                    status = "Neutral";
                    trend = $"En el promedio ({pctDiff:+0.0;-0.0;0.0}%)";
                }

                metricsList.Add(new GroupBenchmarkMetricDto(
                    dimension,
                    athleteVal,
                    groupAvg,
                    unit,
                    diff,
                    pctDiff,
                    direction,
                    status,
                    false, // Se calcula al final
                    trend));
            }
            else
            {
                // Menos de 3 compañeros: k-anonimato activo
                // Promedio, diferencia y porcentaje en null; status InsufficientGroupData; trend en null
                metricsList.Add(new GroupBenchmarkMetricDto(
                    dimension,
                    athleteVal,
                    null,
                    unit,
                    null,
                    null,
                    direction,
                    "InsufficientGroupData",
                    false,
                    null));
            }
        }

        // 6. Detectar el punto más débil (mayor desventaja porcentual relativa entre los BelowAverage)
        var weakestCandidate = metricsList
            .Where(m => m.Status == "BelowAverage" && m.PercentageDifference.HasValue)
            .OrderBy(m => m.PercentageDifference!.Value)
            .FirstOrDefault();

        if (weakestCandidate is not null)
        {
            var idx = metricsList.IndexOf(weakestCandidate);
            metricsList[idx] = weakestCandidate with { IsWeakestPoint = true };
        }

        return new GroupBenchmarkResponseDto(
            query.AthleteId,
            targetCategoryId,
            targetCategoryName,
            metricsList);
    }

    private static string DetectDirection(string unit, string testName)
    {
        var lowerUnit = unit.Trim().ToLowerInvariant();
        var lowerName = testName.Trim().ToLowerInvariant();

        if (lowerUnit is "s" or "seg" or "segundos" or "min" or "minutos" or "ms"
            || lowerName.Contains("sprint")
            || lowerName.Contains("tiempo")
            || lowerName.Contains("velocidad") && lowerUnit.Contains("s"))
        {
            return "LowerIsBetter";
        }

        return "HigherIsBetter";
    }
}
