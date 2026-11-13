using Application.DTOs.Evolution;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;

namespace Application.UseCases.Evolution.GetAthleteEvolution;

public sealed class GetAthleteEvolutionQueryHandler(
    ITrainingClient trainingClient,
    IFieldTestResultRepository fieldTestResultRepository,
    ICheckInRepository checkInRepository,
    IAthleteAccessGuard athleteAccessGuard,
    IDailyLoadRepository dailyLoadRepository,
    ICurrentUser currentUser)
{
    public async Task<AthleteEvolutionResponseDto> HandleAsync(
        GetAthleteEvolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        await athleteAccessGuard.EnsureCanReadAsync(query.AthleteId, cancellationToken);

        var today = ArgentinaTime.Today;

        var toDate = query.To ?? today;
        var months = query.Months.GetValueOrDefault(3);
        if (months <= 0)
            months = 3;

        var fromDate = query.From ?? toDate.AddMonths(-months);

        var fromDateTime = ArgentinaTime.ToUtcStartOfDay(fromDate);
        var toDateTime = ArgentinaTime.ToUtcEndOfDay(toDate);

        var trainingResult = await trainingClient.GetAthleteRecordsAsync(
            query.AthleteId,
            fromDate,
            toDate,
            cancellationToken);

        var records = trainingResult.Records;

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
                g.GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.AchievedAt, DateTimeKind.Utc), ArgentinaTime.ZonaArgentina)))
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
                g.GroupBy(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.RecordedAt, DateTimeKind.Utc), ArgentinaTime.ZonaArgentina)))
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

        // 4. Series de carga semanal acumulada (solo para PreparadorFisico)
        IReadOnlyList<WeeklyLoadEvolutionPointDto> weeklyLoads;
        if (currentUser.IsInRole("PreparadorFisico"))
        {
            var dailyLoads = await dailyLoadRepository.GetByAthleteAndDateRangeAsync(
                query.AthleteId,
                fromDate,
                toDate,
                cancellationToken);

            var loadsByWeek = dailyLoads
                .GroupBy(load => GetWeekStart(ToArgentinaDate(load.Date)))
                .ToDictionary(g => g.Key, g => g.ToList());

            var startMonday = GetWeekStart(fromDate);
            var points = new List<WeeklyLoadEvolutionPointDto>();

            if (fromDate <= toDate)
            {
                for (var weekStart = startMonday; weekStart <= toDate; weekStart = weekStart.AddDays(7))
                {
                    var weekEnd = weekStart.AddDays(6);
                    if (loadsByWeek.TryGetValue(weekStart, out var weekLoads))
                    {
                        points.Add(new WeeklyLoadEvolutionPointDto(
                            weekStart,
                            weekEnd,
                            weekLoads.Sum(l => l.TotalLoad),
                            weekLoads.Sum(l => l.TotalMinutes)));
                    }
                    else
                    {
                        points.Add(new WeeklyLoadEvolutionPointDto(
                            weekStart,
                            weekEnd,
                            0,
                            0));
                    }
                }
            }

            weeklyLoads = points;
        }
        else
        {
            weeklyLoads = Array.Empty<WeeklyLoadEvolutionPointDto>();
        }

        return new AthleteEvolutionResponseDto(
            query.AthleteId,
            fromDate,
            toDate,
            strengthSeries,
            fitnessSeries,
            wellness,
            weeklyLoads,
            trainingResult.IsUnavailable);
    }

    private static DateOnly GetWeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static DateOnly ToArgentinaDate(DateTime date)
    {
        if (date.Kind == DateTimeKind.Utc && date.TimeOfDay != TimeSpan.Zero)
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(date, ArgentinaTime.ZonaArgentina));

        return DateOnly.FromDateTime(date);
    }
}
