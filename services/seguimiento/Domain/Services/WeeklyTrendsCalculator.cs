using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Domain.Services;

/// <summary>
/// Motor puro de cálculo de tendencias semanales de wellness y carga UA Foster (KAN-489 · #194).
/// Agrupa registros por semanas de lunes a domingo según la zona horaria de Argentina.
/// </summary>
public static class WeeklyTrendsCalculator
{
    public const int DefaultWeeks = 4;
    public const int MaxWeeks = 24;
    public const int MaxRangeDays = 180;

    public sealed record ResolvedWeekWindow(
        DateOnly From,
        DateOnly To,
        IReadOnlyList<(DateOnly WeekStart, DateOnly WeekEnd)> Weeks);

    public sealed record WellnessBreakdownResult(
        double SleepQuality,
        double SleepHours,
        double MuscleSoreness,
        double FatigueLevel,
        double MoodRate,
        double StressLevel);

    public sealed record AthleteWeekTrendResult(
        DateOnly WeekStartDate,
        DateOnly WeekEndDate,
        double? AverageWellness,
        int? TotalLoad,
        int? TotalMinutes,
        int CheckInCount,
        int LoadCount,
        WellnessBreakdownResult? Breakdown);

    public sealed record CategoryWeekTrendResult(
        DateOnly WeekStartDate,
        DateOnly WeekEndDate,
        double? AverageWellness,
        int? TotalLoad,
        int? TotalMinutes,
        int CheckInCount,
        int LoadCount,
        int ActiveReportingAthletes,
        WellnessBreakdownResult? Breakdown);

    /// <summary>
    /// Calcula el lunes de la semana a la que pertenece <paramref name="date"/>.
    /// </summary>
    public static DateOnly GetWeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Convierte un <see cref="DateTime"/> a fecha calendario de Argentina.
    /// Los valores almacenados como fecha pura (00:00:00) conservan su día calendario sin desplazamiento.
    /// </summary>
    public static DateOnly ToArgentinaDate(DateTime date)
    {
        if (date.Kind == DateTimeKind.Utc && date.TimeOfDay != TimeSpan.Zero)
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(date, ArgentinaTime.ZonaArgentina));

        return DateOnly.FromDateTime(date);
    }

    /// <summary>
    /// Resuelve la ventana semanal a consultar. Por defecto devuelve las últimas 4 semanas (lunes a domingo),
    /// incluyendo la semana actual hasta <paramref name="todayArgentina"/>.
    /// </summary>
    public static ResolvedWeekWindow ResolveWindow(
        DateOnly todayArgentina,
        int? weeks,
        DateOnly? from,
        DateOnly? to)
    {
        if (weeks.HasValue && (weeks.Value < 1 || weeks.Value > MaxWeeks))
            throw new DomainException($"La cantidad de semanas debe estar entre 1 y {MaxWeeks}.");

        DateOnly resolvedFrom;
        DateOnly resolvedTo;

        if (from.HasValue || to.HasValue)
        {
            resolvedTo = to ?? todayArgentina;
            var weekCount = weeks ?? DefaultWeeks;
            resolvedFrom = from ?? GetWeekStart(resolvedTo).AddDays(-7 * (weekCount - 1));

            if (resolvedFrom > resolvedTo)
                throw new DomainException("La fecha 'from' no puede ser posterior a la fecha 'to'.");

            if (resolvedTo.DayNumber - resolvedFrom.DayNumber > MaxRangeDays)
                throw new DomainException($"El rango máximo permitido es de {MaxRangeDays} días.");
        }
        else
        {
            var weekCount = weeks ?? DefaultWeeks;
            resolvedTo = todayArgentina;
            resolvedFrom = GetWeekStart(todayArgentina).AddDays(-7 * (weekCount - 1));
        }

        var firstMonday = GetWeekStart(resolvedFrom);
        var lastMonday = GetWeekStart(resolvedTo);
        var weekList = new List<(DateOnly WeekStart, DateOnly WeekEnd)>();

        for (var monday = firstMonday; monday <= lastMonday; monday = monday.AddDays(7))
        {
            weekList.Add((monday, monday.AddDays(6)));
        }

        return new ResolvedWeekWindow(resolvedFrom, resolvedTo, weekList);
    }

    /// <summary>
    /// Índice compuesto diario de wellness (escala 1 a 5):
    /// SleepQuality y MoodRate suman directo; FatigueLevel, MuscleSoreness y StressLevel se invierten (6 - valor).
    /// </summary>
    public static double CalculateDailyWellness(CheckIn checkIn) =>
        (checkIn.SleepQuality
         + checkIn.MoodRate
         + (6 - checkIn.FatigueLevel)
         + (6 - checkIn.MuscleSoreness)
         + (6 - checkIn.StressLevel)) / 5.0;

    /// <summary>
    /// Calcula las tendencias semanales individuales de un atleta en orden cronológico ascendente.
    /// </summary>
    public static IReadOnlyList<AthleteWeekTrendResult> ComputeAthleteTrends(
        ResolvedWeekWindow window,
        IReadOnlyList<CheckIn> checkIns,
        IReadOnlyList<DailyLoad> dailyLoads)
    {
        var checkInsByWeek = checkIns
            .GroupBy(c => GetWeekStart(ToArgentinaDate(c.Date)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var loadsByWeek = dailyLoads
            .GroupBy(l => GetWeekStart(ToArgentinaDate(l.Date)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<AthleteWeekTrendResult>(window.Weeks.Count);

        foreach (var (weekStart, weekEnd) in window.Weeks)
        {
            checkInsByWeek.TryGetValue(weekStart, out var weekCheckIns);
            loadsByWeek.TryGetValue(weekStart, out var weekLoads);

            var checkInCount = weekCheckIns?.Count ?? 0;
            var loadCount = weekLoads?.Count ?? 0;

            double? averageWellness = checkInCount > 0
                ? Round1(weekCheckIns!.Average(CalculateDailyWellness))
                : null;

            WellnessBreakdownResult? breakdown = checkInCount > 0
                ? ComputeAthleteBreakdown(weekCheckIns!)
                : null;

            int? totalLoad = loadCount > 0 ? weekLoads!.Sum(l => l.TotalLoad) : null;
            int? totalMinutes = loadCount > 0 ? weekLoads!.Sum(l => l.TotalMinutes) : null;

            results.Add(new AthleteWeekTrendResult(
                weekStart,
                weekEnd,
                averageWellness,
                totalLoad,
                totalMinutes,
                checkInCount,
                loadCount,
                breakdown));
        }

        return results;
    }

    /// <summary>
    /// Calcula las tendencias semanales grupales de una categoría.
    /// El wellness grupal usa G2: promedio de los promedios semanales individuales (mismo peso por atleta).
    /// La carga grupal suma las cargas semanales de los atletas distintos de la categoría.
    /// </summary>
    public static IReadOnlyList<CategoryWeekTrendResult> ComputeCategoryTrends(
        ResolvedWeekWindow window,
        IReadOnlyCollection<Guid> distinctCategoryAthleteIds,
        IReadOnlyList<CheckIn> checkIns,
        IReadOnlyList<DailyLoad> dailyLoads)
    {
        var athleteSet = distinctCategoryAthleteIds.ToHashSet();

        var checkInsByWeek = checkIns
            .Where(c => athleteSet.Contains(c.AthleteId))
            .GroupBy(c => GetWeekStart(ToArgentinaDate(c.Date)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var loadsByWeek = dailyLoads
            .Where(l => athleteSet.Contains(l.AthleteId))
            .GroupBy(l => GetWeekStart(ToArgentinaDate(l.Date)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<CategoryWeekTrendResult>(window.Weeks.Count);

        foreach (var (weekStart, weekEnd) in window.Weeks)
        {
            checkInsByWeek.TryGetValue(weekStart, out var weekCheckIns);
            loadsByWeek.TryGetValue(weekStart, out var weekLoads);

            var checkInCount = weekCheckIns?.Count ?? 0;
            var loadCount = weekLoads?.Count ?? 0;

            double? averageWellness = null;
            WellnessBreakdownResult? breakdown = null;
            var activeReportingAthletes = 0;

            if (checkInCount > 0)
            {
                var byAthlete = weekCheckIns!
                    .GroupBy(c => c.AthleteId)
                    .ToList();

                activeReportingAthletes = byAthlete.Count;

                // G2: cada atleta aporta su promedio semanal con igual peso dentro de la categoría.
                averageWellness = Round1(byAthlete.Average(g => g.Average(CalculateDailyWellness)));
                breakdown = new WellnessBreakdownResult(
                    SleepQuality: Round1(byAthlete.Average(g => g.Average(c => (double)c.SleepQuality))),
                    SleepHours: Round1(byAthlete.Average(g => g.Average(c => c.SleepHours))),
                    MuscleSoreness: Round1(byAthlete.Average(g => g.Average(c => (double)c.MuscleSoreness))),
                    FatigueLevel: Round1(byAthlete.Average(g => g.Average(c => (double)c.FatigueLevel))),
                    MoodRate: Round1(byAthlete.Average(g => g.Average(c => (double)c.MoodRate))),
                    StressLevel: Round1(byAthlete.Average(g => g.Average(c => (double)c.StressLevel))));
            }

            int? totalLoad = loadCount > 0 ? weekLoads!.Sum(l => l.TotalLoad) : null;
            int? totalMinutes = loadCount > 0 ? weekLoads!.Sum(l => l.TotalMinutes) : null;

            results.Add(new CategoryWeekTrendResult(
                weekStart,
                weekEnd,
                averageWellness,
                totalLoad,
                totalMinutes,
                checkInCount,
                loadCount,
                activeReportingAthletes,
                breakdown));
        }

        return results;
    }

    private static WellnessBreakdownResult ComputeAthleteBreakdown(IReadOnlyList<CheckIn> checkIns) =>
        new(
            SleepQuality: Round1(checkIns.Average(c => (double)c.SleepQuality)),
            SleepHours: Round1(checkIns.Average(c => c.SleepHours)),
            MuscleSoreness: Round1(checkIns.Average(c => (double)c.MuscleSoreness)),
            FatigueLevel: Round1(checkIns.Average(c => (double)c.FatigueLevel)),
            MoodRate: Round1(checkIns.Average(c => (double)c.MoodRate)),
            StressLevel: Round1(checkIns.Average(c => (double)c.StressLevel)));

    private static double Round1(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);
}
