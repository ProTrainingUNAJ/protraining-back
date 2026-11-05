using Application.DTOs.Evolution;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Evolution.GetAthleteEvolution;

public sealed class GetAthleteEvolutionQueryHandler(
    ITrainingClient trainingClient,
    IFieldTestResultRepository fieldTestResultRepository,
    ICheckInRepository checkInRepository)
{
    private static readonly TimeZoneInfo ArgentinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    public async Task<AthleteEvolutionResponseDto> HandleAsync(
        GetAthleteEvolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        var nowInArgentina = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ArgentinaTimeZone);
        var today = DateOnly.FromDateTime(nowInArgentina);

        var toDate = query.To ?? today;
        var months = query.Months.GetValueOrDefault(3);
        if (months <= 0)
            months = 3;

        var fromDate = query.From ?? toDate.AddMonths(-months);

        var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDateTime = toDate.ToDateTime(new TimeOnly(23, 59, 59, 999), DateTimeKind.Utc);

        var records = await trainingClient.GetAthleteRecordsAsync(
            query.AthleteId,
            fromDate,
            toDate,
            cancellationToken);

        var fitnessTests = await fieldTestResultRepository.ListByAthleteAndDateRangeAsync(
            query.AthleteId,
            catalogTestId: null,
            from: fromDateTime,
            to: toDateTime,
            cancellationToken: cancellationToken);

        var checkIns = await checkInRepository.GetByAthleteAndDateRangeAsync(
            query.AthleteId,
            startDate: fromDateTime,
            endDate: toDateTime,
            limit: null,
            cancellationToken: cancellationToken);

        // 1. Curvas de Fuerza (1RM) agrupadas por ejercicio
        var strengthSeries = records
            .GroupBy(r => (r.ExerciseId, r.ExerciseName))
            .Select(g => new StrengthEvolutionSeriesDto(
                g.Key.ExerciseId,
                g.Key.ExerciseName,
                "kg",
                g.GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.AchievedAt, DateTimeKind.Utc), ArgentinaTimeZone)))
                 .Select(dg =>
                 {
                     var best = dg.MaxBy(r => r.WeightOneRepMaxKg)!;
                     return new EvolutionDataPointDto(
                         dg.Key,
                         (double)best.WeightOneRepMaxKg,
                         $"{best.WeightOneRepMaxKg} kg");
                 })
                 .OrderBy(p => p.Date)
                 .ToList()))
            .OrderBy(s => s.ExerciseName)
            .ToList();

        // 2. Curvas de Capacidades Atléticas (salto, velocidad, etc.) agrupadas por test físico
        var fitnessSeries = fitnessTests
            .GroupBy(t => (t.CatalogTestId, t.TestName, t.MotorCapacity, t.MeasurementUnit))
            .Select(g => new FitnessTestEvolutionSeriesDto(
                g.Key.CatalogTestId,
                g.Key.TestName,
                g.Key.MotorCapacity,
                g.Key.MeasurementUnit,
                g.GroupBy(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.RecordedAt, DateTimeKind.Utc), ArgentinaTimeZone)))
                 .Select(dg =>
                 {
                     var isSpeed = dg.First().MotorCapacity.Equals("Speed", StringComparison.OrdinalIgnoreCase);
                     var best = isSpeed ? dg.MinBy(t => t.Value)! : dg.MaxBy(t => t.Value)!;
                     return new EvolutionDataPointDto(
                         dg.Key,
                         (double)best.Value,
                         $"{best.Value} {g.Key.MeasurementUnit}");
                 })
                 .OrderBy(p => p.Date)
                 .ToList()))
            .OrderBy(s => s.TestName)
            .ToList();

        // 3. Curvas de Fatiga / Wellness día a día
        var orderedCheckIns = checkIns
            .OrderBy(c => c.Date)
            .ToList();

        var fatiguePoints = orderedCheckIns
            .Select(c => new EvolutionDataPointDto(
                DateOnly.FromDateTime(c.Date),
                c.FatigueLevel,
                $"Nivel {c.FatigueLevel}"))
            .ToList();

        var dailyPoints = orderedCheckIns
            .Select(c => new DailyWellnessPointDto(
                DateOnly.FromDateTime(c.Date),
                c.FatigueLevel,
                c.SleepHours,
                c.SleepQuality,
                c.MuscleSoreness,
                c.MoodRate,
                c.StressLevel,
                c.TrafficLight.ToString(),
                c.HasActiveInjury))
            .ToList();

        var wellness = new WellnessEvolutionDto(fatiguePoints, dailyPoints);

        return new AthleteEvolutionResponseDto(
            query.AthleteId,
            fromDate,
            toDate,
            strengthSeries,
            fitnessSeries,
            wellness);
    }
}
