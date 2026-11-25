using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;

namespace Seguimiento.Tests;

/// <summary>
/// Pruebas unitarias puras de WeeklyTrendsCalculator (KAN-489 · #194).
/// Validan fórmulas W1 (wellness compuesto), G2 (promedio grupal por atleta), UA Foster y límites temporales.
/// </summary>
public sealed class WeeklyTrendsCalculatorTests
{
    private static CheckIn CreateCheckIn(
        Guid athleteId,
        DateTime date,
        int sleepQuality = 4,
        double sleepHours = 8.0,
        int muscleSoreness = 2,
        int fatigueLevel = 2,
        int moodRate = 4,
        int stressLevel = 2) =>
        new()
        {
            AthleteId = athleteId,
            Date = date,
            TrafficLight = TrafficLight.Green,
            SleepHours = sleepHours,
            SleepQuality = sleepQuality,
            MuscleSoreness = muscleSoreness,
            FatigueLevel = fatigueLevel,
            MoodRate = moodRate,
            StressLevel = stressLevel
        };

    private static DailyLoad CreateDailyLoad(
        Guid athleteId,
        DateOnly date,
        int gymMinutes = 0,
        int? gymRpe = null,
        int sportMinutes = 0,
        int? sportRpe = null) =>
        DailyLoad.Register(
            athleteId,
            date.ToDateTime(TimeOnly.MinValue),
            gymMinutes,
            gymRpe,
            sportMinutes,
            sportRpe,
            0,
            null,
            0,
            null,
            0,
            null);

    [Fact]
    public void KAN489_01_FourWeeksWithData_ReturnsExactlyFourChronologicalMondayToSundayPoints()
    {
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 17); // Martes -> semana actual inicia lunes 2026-11-16
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 4, from: null, to: null);

        var checkIns = new[]
        {
            CreateCheckIn(athleteId, new DateTime(2026, 10, 27)), // Semana 1 (2026-10-26..2026-11-01)
            CreateCheckIn(athleteId, new DateTime(2026, 11, 4)),  // Semana 2 (2026-11-02..2026-11-08)
            CreateCheckIn(athleteId, new DateTime(2026, 11, 12)), // Semana 3 (2026-11-09..2026-11-15)
            CreateCheckIn(athleteId, new DateTime(2026, 11, 17))  // Semana 4 (2026-11-16..2026-11-22)
        };

        var loads = new[]
        {
            CreateDailyLoad(athleteId, new DateOnly(2026, 10, 28), gymMinutes: 60, gymRpe: 6),
            CreateDailyLoad(athleteId, new DateOnly(2026, 11, 5), sportMinutes: 90, sportRpe: 7),
            CreateDailyLoad(athleteId, new DateOnly(2026, 11, 11), gymMinutes: 45, gymRpe: 8),
            CreateDailyLoad(athleteId, new DateOnly(2026, 11, 16), sportMinutes: 60, sportRpe: 5)
        };

        var points = WeeklyTrendsCalculator.ComputeAthleteTrends(window, checkIns, loads);

        Assert.Equal(4, points.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), points[0].WeekStartDate);
        Assert.Equal(new DateOnly(2026, 11, 1), points[0].WeekEndDate);
        Assert.Equal(new DateOnly(2026, 11, 2), points[1].WeekStartDate);
        Assert.Equal(new DateOnly(2026, 11, 9), points[2].WeekStartDate);
        Assert.Equal(new DateOnly(2026, 11, 16), points[3].WeekStartDate);
        Assert.Equal(new DateOnly(2026, 11, 22), points[3].WeekEndDate);
        Assert.All(points, p =>
        {
            Assert.Equal(1, p.CheckInCount);
            Assert.Equal(1, p.LoadCount);
            Assert.NotNull(p.AverageWellness);
            Assert.NotNull(p.TotalLoad);
        });
    }

    [Fact]
    public void KAN489_02_WellnessFormulaW1_InvertsNegativeScalesAndExcludesSleepHoursFromScore()
    {
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 18);
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 1, from: null, to: null);

        // Día 1: SleepQuality=5, MoodRate=4, Fatigue=1 (6-1=5), Soreness=2 (6-2=4), Stress=3 (6-3=3) -> (5+4+5+4+3)/5 = 4.2
        // Día 2: SleepQuality=2, MoodRate=3, Fatigue=4 (6-4=2), Soreness=5 (6-5=1), Stress=2 (6-2=4) -> (2+3+2+1+4)/5 = 2.4
        // Promedio semanal = (4.2 + 2.4) / 2 = 3.3 (SleepHours distintos no alteran el índice compuesto)
        var checkIns = new[]
        {
            CreateCheckIn(athleteId, new DateTime(2026, 11, 16), sleepQuality: 5, sleepHours: 4.0, muscleSoreness: 2, fatigueLevel: 1, moodRate: 4, stressLevel: 3),
            CreateCheckIn(athleteId, new DateTime(2026, 11, 17), sleepQuality: 2, sleepHours: 10.0, muscleSoreness: 5, fatigueLevel: 4, moodRate: 3, stressLevel: 2)
        };

        var points = WeeklyTrendsCalculator.ComputeAthleteTrends(window, checkIns, []);

        var week = Assert.Single(points);
        Assert.Equal(3.3, week.AverageWellness);
        Assert.Equal(2, week.CheckInCount);
        Assert.NotNull(week.Breakdown);
        Assert.Equal(3.5, week.Breakdown.SleepQuality);
        Assert.Equal(7.0, week.Breakdown.SleepHours);
    }

    [Fact]
    public void KAN489_03_WeeklyLoad_SumsUaFosterMinutesTimesRpeWithinWeek()
    {
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 18);
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 1, from: null, to: null);

        // Día 1: Gym 60*7 = 420 + Sport 90*8 = 720 -> 1140 (150 min)
        // Día 2: Sport 60*5 = 300 (60 min)
        // Total semana = 1440 UA, 210 min
        var loads = new[]
        {
            CreateDailyLoad(athleteId, new DateOnly(2026, 11, 16), gymMinutes: 60, gymRpe: 7, sportMinutes: 90, sportRpe: 8),
            CreateDailyLoad(athleteId, new DateOnly(2026, 11, 18), sportMinutes: 60, sportRpe: 5)
        };

        var points = WeeklyTrendsCalculator.ComputeAthleteTrends(window, [], loads);

        var week = Assert.Single(points);
        Assert.Equal(1440, week.TotalLoad);
        Assert.Equal(210, week.TotalMinutes);
        Assert.Equal(2, week.LoadCount);
    }

    [Fact]
    public void KAN489_04_CategoryTrends_UsesEqualAthleteWeightG2AndSumsGroupLoads()
    {
        var athleteA = Guid.NewGuid();
        var athleteB = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 18);
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 1, from: null, to: null);

        // Atleta A: 4 check-ins con wellness perfecto 5.0 (5,5,1,1,1) -> promedio individual = 5.0
        // Atleta B: 1 check-in con wellness mínimo 1.0 (1,1,5,5,5) -> promedio individual = 1.0
        // Con G2 (peso igual por atleta): (5.0 + 1.0) / 2 = 3.0 (en promedio directo de check-ins sería 4.2).
        var checkIns = new[]
        {
            CreateCheckIn(athleteA, new DateTime(2026, 11, 16), 5, 8, 1, 1, 5, 1),
            CreateCheckIn(athleteA, new DateTime(2026, 11, 17), 5, 8, 1, 1, 5, 1),
            CreateCheckIn(athleteA, new DateTime(2026, 11, 18), 5, 8, 1, 1, 5, 1),
            CreateCheckIn(athleteA, new DateTime(2026, 11, 19), 5, 8, 1, 1, 5, 1),
            CreateCheckIn(athleteB, new DateTime(2026, 11, 16), 1, 5, 5, 5, 1, 5)
        };

        var loads = new[]
        {
            CreateDailyLoad(athleteA, new DateOnly(2026, 11, 16), gymMinutes: 60, gymRpe: 5),   // 300
            CreateDailyLoad(athleteB, new DateOnly(2026, 11, 17), sportMinutes: 50, sportRpe: 8) // 400
        };

        var points = WeeklyTrendsCalculator.ComputeCategoryTrends(window, [athleteA, athleteB], checkIns, loads);

        var week = Assert.Single(points);
        Assert.Equal(3.0, week.AverageWellness);
        Assert.Equal(700, week.TotalLoad);
        Assert.Equal(110, week.TotalMinutes);
        Assert.Equal(5, week.CheckInCount);
        Assert.Equal(2, week.LoadCount);
        Assert.Equal(2, week.ActiveReportingAthletes);
    }

    [Fact]
    public void KAN489_05_ManyToManyAthletes_DeduplicatesWithinCategoryAndIncludesInBothCategories()
    {
        var sharedAthlete = Guid.NewGuid();
        var otherAthlete = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 18);
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 1, from: null, to: null);

        var checkIns = new[]
        {
            CreateCheckIn(sharedAthlete, new DateTime(2026, 11, 16), 5, 8, 1, 1, 5, 1), // 5.0
            CreateCheckIn(otherAthlete, new DateTime(2026, 11, 16), 3, 7, 3, 3, 3, 3)   // 3.0
        };
        var loads = new[]
        {
            CreateDailyLoad(sharedAthlete, new DateOnly(2026, 11, 16), gymMinutes: 60, gymRpe: 5), // 300
            CreateDailyLoad(otherAthlete, new DateOnly(2026, 11, 16), gymMinutes: 40, gymRpe: 5)   // 200
        };

        // Incluso si llegara duplicado el Guid en la categoría 1, no duplica carga ni conteos.
        var cat1 = WeeklyTrendsCalculator.ComputeCategoryTrends(
            window, [sharedAthlete, sharedAthlete, otherAthlete], checkIns, loads);
        var cat2 = WeeklyTrendsCalculator.ComputeCategoryTrends(
            window, [sharedAthlete], checkIns, loads);

        Assert.Equal(500, cat1[0].TotalLoad);
        Assert.Equal(4.0, cat1[0].AverageWellness);
        Assert.Equal(2, cat1[0].ActiveReportingAthletes);

        Assert.Equal(300, cat2[0].TotalLoad);
        Assert.Equal(5.0, cat2[0].AverageWellness);
        Assert.Equal(1, cat2[0].ActiveReportingAthletes);
    }

    [Fact]
    public void KAN489_08_EmptyWeeks_ReturnNullMetricsAndZeroCountsDistinguishingFromZeroLoad()
    {
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 17);
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 4, from: null, to: null);

        // Solo semana 1 tiene carga con RPE 0 (TotalLoad = 0, LoadCount = 1). Semanas 2..4 sin datos.
        var loads = new[]
        {
            CreateDailyLoad(athleteId, new DateOnly(2026, 10, 27), gymMinutes: 30, gymRpe: 0)
        };

        var points = WeeklyTrendsCalculator.ComputeAthleteTrends(window, [], loads);

        Assert.Equal(4, points.Count);
        Assert.Equal(0, points[0].TotalLoad);
        Assert.Equal(30, points[0].TotalMinutes);
        Assert.Equal(1, points[0].LoadCount);
        Assert.Null(points[0].AverageWellness);
        Assert.Equal(0, points[0].CheckInCount);

        for (var i = 1; i < 4; i++)
        {
            Assert.Null(points[i].AverageWellness);
            Assert.Null(points[i].Breakdown);
            Assert.Null(points[i].TotalLoad);
            Assert.Null(points[i].TotalMinutes);
            Assert.Equal(0, points[i].CheckInCount);
            Assert.Equal(0, points[i].LoadCount);
        }
    }

    [Fact]
    public void KAN489_09_SundayNightAndMondayMorningArgentina_AssignToRespectiveWeeks()
    {
        var athleteId = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 17);
        var window = WeeklyTrendsCalculator.ResolveWindow(today, weeks: 2, from: null, to: null);

        // Domingo 2026-11-15 23:30 Argentina = Lunes 2026-11-16 02:30 UTC -> debe ir a semana 2026-11-09..15.
        // Lunes 2026-11-16 00:15 Argentina = Lunes 2026-11-16 03:15 UTC -> debe ir a semana 2026-11-16..22.
        var sundayNightUtc = new DateTime(2026, 11, 16, 2, 30, 0, DateTimeKind.Utc);
        var mondayMorningUtc = new DateTime(2026, 11, 16, 3, 15, 0, DateTimeKind.Utc);

        var checkIns = new[]
        {
            CreateCheckIn(athleteId, sundayNightUtc, sleepQuality: 2, moodRate: 2, fatigueLevel: 4, muscleSoreness: 4, stressLevel: 4), // wellness 2.0
            CreateCheckIn(athleteId, mondayMorningUtc, sleepQuality: 5, moodRate: 5, fatigueLevel: 1, muscleSoreness: 1, stressLevel: 1) // wellness 5.0
        };

        var points = WeeklyTrendsCalculator.ComputeAthleteTrends(window, checkIns, []);

        Assert.Equal(2, points.Count);
        Assert.Equal(new DateOnly(2026, 11, 9), points[0].WeekStartDate);
        Assert.Equal(2.0, points[0].AverageWellness);
        Assert.Equal(1, points[0].CheckInCount);

        Assert.Equal(new DateOnly(2026, 11, 16), points[1].WeekStartDate);
        Assert.Equal(5.0, points[1].AverageWellness);
        Assert.Equal(1, points[1].CheckInCount);
    }

    [Fact]
    public void ResolveWindow_InvalidArguments_ThrowDomainException()
    {
        var today = new DateOnly(2026, 11, 17);

        Assert.Throws<DomainException>(() => WeeklyTrendsCalculator.ResolveWindow(today, weeks: 0, from: null, to: null));
        Assert.Throws<DomainException>(() => WeeklyTrendsCalculator.ResolveWindow(today, weeks: 25, from: null, to: null));
        Assert.Throws<DomainException>(() => WeeklyTrendsCalculator.ResolveWindow(today, weeks: null, from: new DateOnly(2026, 11, 10), to: new DateOnly(2026, 11, 1)));
    }
}
