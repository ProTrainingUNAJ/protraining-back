using Application.DTOs.Averages;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;

namespace Application.UseCases.Categories.GetCategoryAverages;

public sealed record GetCategoryAveragesQuery(
    Guid CategoryId,
    string? WeekStart = null);

public sealed class GetCategoryAveragesQueryHandler(
    IStructureClient structureClient,
    ICheckInRepository checkInRepository,
    IFitnessTestMeasurementService fitnessTestMeasurementService)
{
    public async Task<CategoryAveragesDto> HandleAsync(
        GetCategoryAveragesQuery query,
        CancellationToken cancellationToken = default)
    {
        // 1. Cálculo del rango semanal (Lunes a Domingo en huso horario de Argentina)
        DateOnly anchor;
        if (!string.IsNullOrWhiteSpace(query.WeekStart) && DateOnly.TryParse(query.WeekStart, out var parsed))
        {
            anchor = parsed;
        }
        else
        {
            anchor = ArgentinaTime.Today;
        }

        var weekStart = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(6);

        // 2. Obtener los atletas activos de la categoría desde Estructura
        // Si la categoría no existe o es de otro PF, Estructura responde 404 -> CategoryNotFoundException -> 404
        var athletes = await structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);
        var athleteIds = athletes
            .Select(a => a.Id)
            .Distinct()
            .ToList();

        if (athleteIds.Count == 0)
        {
            return new CategoryAveragesDto(
                query.CategoryId,
                0,
                weekStart,
                weekEnd,
                Array.Empty<FitnessTestAverageDto>(),
                Array.Empty<WellnessMetricAverageDto>());
        }

        // 3. Tests Físicos: obtener mediciones y promediar por tipo y unidad
        var testMeasurements = await fitnessTestMeasurementService.GetMeasurementsByAthletesAsync(
            athleteIds,
            cancellationToken);

        var testAverages = new List<FitnessTestAverageDto>();

        var validMeasurements = testMeasurements
            .Where(m => athleteIds.Contains(m.AthleteId) && m.Value >= 0)
            .GroupBy(m => (m.TestType, m.Unit));

        foreach (var group in validMeasurements.OrderBy(g => g.Key.TestType))
        {
            // Medición más reciente por atleta para este test
            var latestPerAthlete = group
                .GroupBy(m => m.AthleteId)
                .Select(athleteGroup => (
                    AthleteId: athleteGroup.Key,
                    LatestValue: athleteGroup.OrderByDescending(m => m.Date).First().Value
                ))
                .ToList();

            if (latestPerAthlete.Count > 0)
            {
                var avg = Math.Round(latestPerAthlete.Average(x => x.LatestValue), 1, MidpointRounding.AwayFromZero);
                testAverages.Add(new FitnessTestAverageDto(
                    group.Key.TestType,
                    group.Key.Unit,
                    avg,
                    latestPerAthlete.Count));
            }
        }

        // 4. Wellness: obtener check-ins dentro de la semana solicitada
        var startDateTime = weekStart.ToDateTime(TimeOnly.MinValue);
        var endDateTime = weekEnd.ToDateTime(new TimeOnly(23, 59, 59));

        var checkIns = await checkInRepository.GetByAthletesAndDateRangeAsync(
            athleteIds,
            startDateTime,
            endDateTime,
            cancellationToken);

        var validCheckIns = checkIns
            .Where(c => athleteIds.Contains(c.AthleteId))
            .ToList();

        var wellnessAverages = new List<WellnessMetricAverageDto>();

        if (validCheckIns.Count > 0)
        {
            AddWellnessMetric(wellnessAverages, "SleepQuality", validCheckIns.Select(c => (c.AthleteId, (double?)c.SleepQuality)));
            AddWellnessMetric(wellnessAverages, "SleepHours", validCheckIns.Select(c => (c.AthleteId, (double?)c.SleepHours)));
            AddWellnessMetric(wellnessAverages, "MuscleSoreness", validCheckIns.Select(c => (c.AthleteId, (double?)c.MuscleSoreness)));
            AddWellnessMetric(wellnessAverages, "FatigueLevel", validCheckIns.Select(c => (c.AthleteId, (double?)c.FatigueLevel)));
            AddWellnessMetric(wellnessAverages, "MoodRate", validCheckIns.Select(c => (c.AthleteId, (double?)c.MoodRate)));
            AddWellnessMetric(wellnessAverages, "StressLevel", validCheckIns.Select(c => (c.AthleteId, (double?)c.StressLevel)));
            AddWellnessMetric(wellnessAverages, "EffortRate", validCheckIns.Select(c => (c.AthleteId, (double?)c.EffortRate)));
        }

        return new CategoryAveragesDto(
            query.CategoryId,
            athleteIds.Count,
            weekStart,
            weekEnd,
            testAverages,
            wellnessAverages);
    }

    private static void AddWellnessMetric(
        List<WellnessMetricAverageDto> list,
        string metricName,
        IEnumerable<(Guid AthleteId, double? Value)> values)
    {
        var reported = values
            .Where(v => v.Value.HasValue)
            .Select(v => (v.AthleteId, Value: v.Value!.Value))
            .ToList();

        if (reported.Count > 0)
        {
            var avg = Math.Round(reported.Average(r => r.Value), 1, MidpointRounding.AwayFromZero);
            var reportedAthletes = reported.Select(r => r.AthleteId).Distinct().Count();
            list.Add(new WellnessMetricAverageDto(metricName, avg, reportedAthletes, reported.Count));
        }
    }
}
