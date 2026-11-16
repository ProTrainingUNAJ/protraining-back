namespace Asistente.Tests;

using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Xunit;

/// <summary>
/// Pruebas unitarias para el motor determinístico de progresión deportiva en C#.
/// Valida que la IA únicamente propone sesiones modelo y el motor proyecta las semanas 2 a N
/// aplicando sobrecarga progresiva, pico, descarga y tapering pre-partido.
/// </summary>
public sealed class WeeklyProgressionEngineTests
{
    private static PlanDraftSessionDto CreateSampleSession(DayOfWeek day = DayOfWeek.Monday, decimal basePercentage = 80m)
    {
        return new PlanDraftSessionDto(
            Name: "Sesión 1 - Fuerza",
            DayOfWeek: day,
            Order: 1,
            CoachNotes: "Enfoque técnico",
            Exercises:
            [
                new PlanDraftExerciseDto(
                    ExerciseId: Guid.NewGuid(),
                    Order: 1,
                    Sets: 4,
                    Reps: 6,
                    TargetPercentage: basePercentage
                ),
                new PlanDraftExerciseDto(
                    ExerciseId: Guid.NewGuid(),
                    Order: 2,
                    Sets: 3,
                    Reps: 12,
                    TargetPercentage: null // Ejercicio accesorio sin %
                )
            ]
        );
    }

    [Fact]
    public void Given_EmptyModelSessions_When_GeneratingProgression_Then_ReturnsEmptyList()
    {
        // Act
        var result = WeeklyProgressionEngine.GenerateProgressionWeeks([], totalWeeks: 4);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Given_OneWeekPlan_When_GeneratingProgression_Then_ReturnsWeekOneWithBasePercentage()
    {
        // Arrange
        var session = CreateSampleSession(basePercentage: 80m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks([session], totalWeeks: 1);

        // Assert
        Assert.Single(weeks);
        Assert.Equal(1, weeks[0].WeekNumber);
        var exercise = weeks[0].Sessions[0].Exercises[0];
        Assert.Equal(80m, exercise.TargetPercentage);
        Assert.Equal(4, exercise.Sets);
        Assert.Equal(6, exercise.Reps);
    }

    [Fact]
    public void Given_FourWeekPlan_When_GeneratingProgression_Then_WeekFourIsDeloadWithReducedLoad()
    {
        // Arrange
        var session = CreateSampleSession(basePercentage: 80m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks([session], totalWeeks: 4);

        // Assert
        Assert.Equal(4, weeks.Count);

        var week4 = weeks[3];
        Assert.Equal(4, week4.WeekNumber);
        var mainEx = week4.Sessions[0].Exercises[0];
        var accessoryEx = week4.Sessions[0].Exercises[1];

        // En descarga, el porcentaje tiene tope de 65% y las series se reducen
        Assert.True(mainEx.TargetPercentage <= 65m);
        Assert.True(mainEx.Sets < 4);
        Assert.True(accessoryEx.Sets < 3);
        Assert.Contains("descarga", week4.Sessions[0].CoachNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Given_FourWeekPlan_When_GeneratingProgression_Then_WeekThreeIsPeakLoad()
    {
        // Arrange
        var session = CreateSampleSession(basePercentage: 80m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks([session], totalWeeks: 4);

        // Assert
        var week2Main = weeks[1].Sessions[0].Exercises[0];
        var week3Main = weeks[2].Sessions[0].Exercises[0];

        // La semana 3 (pico) debe tener mayor o igual intensidad que la semana 2
        Assert.True(week3Main.TargetPercentage >= week2Main.TargetPercentage);
        Assert.Contains("pico", weeks[2].Sessions[0].CoachNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Given_MatchDateOnDayAfterSession_When_GeneratingProgression_Then_TargetPercentageIsCappedAt60()
    {
        // Arrange: Sesión el lunes 2026-11-16, Partido el martes 2026-11-17 (D-1)
        var sessionDate = new DateOnly(2026, 11, 16);
        var matchDate = new DateOnly(2026, 11, 17);
        var session = CreateSampleSession(day: DayOfWeek.Monday, basePercentage: 80m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
            [session],
            totalWeeks: 1,
            startDate: sessionDate,
            matchDates: [matchDate]);

        // Assert
        var mainEx = weeks[0].Sessions[0].Exercises[0];
        Assert.Equal(60m, mainEx.TargetPercentage);
        Assert.Contains("Tapering D-1", weeks[0].Sessions[0].CoachNotes);
    }

    [Fact]
    public void Given_MatchDateTwoDaysAfterSession_When_GeneratingProgression_Then_TargetPercentageIsCappedAt75()
    {
        // Arrange: Sesión el lunes 2026-11-16, Partido el miércoles 2026-11-18 (D-2)
        var sessionDate = new DateOnly(2026, 11, 16);
        var matchDate = new DateOnly(2026, 11, 18);
        var session = CreateSampleSession(day: DayOfWeek.Monday, basePercentage: 80m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
            [session],
            totalWeeks: 1,
            startDate: sessionDate,
            matchDates: [matchDate]);

        // Assert
        var mainEx = weeks[0].Sessions[0].Exercises[0];
        Assert.Equal(75m, mainEx.TargetPercentage);
        Assert.Contains("Tapering D-2", weeks[0].Sessions[0].CoachNotes);
    }

    [Fact]
    public void Given_MatchDateOnSameDayOfSession_When_GeneratingProgression_Then_SessionIsOmitted()
    {
        // Arrange: Sesión el lunes 2026-11-16, Partido el mismo lunes 2026-11-16 (D)
        var sessionDate = new DateOnly(2026, 11, 16);
        var session = CreateSampleSession(day: DayOfWeek.Monday, basePercentage: 80m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks(
            [session],
            totalWeeks: 1,
            startDate: sessionDate,
            matchDates: [sessionDate]);

        // Assert
        Assert.Single(weeks);
        Assert.Empty(weeks[0].Sessions); // La sesión de fuerza en día D no se programa
    }

    [Fact]
    public void Given_Percentages_When_ProgressionApplied_Then_RoundedToQuarterSteps()
    {
        // Arrange
        var session = CreateSampleSession(basePercentage: 77.3m);

        // Act
        var weeks = WeeklyProgressionEngine.GenerateProgressionWeeks([session], totalWeeks: 2);

        // Assert
        foreach (var week in weeks)
        {
            foreach (var s in week.Sessions)
            {
                foreach (var ex in s.Exercises)
                {
                    if (ex.TargetPercentage.HasValue)
                    {
                        var remainder = ex.TargetPercentage.Value % 2.5m;
                        Assert.Equal(0m, remainder);
                    }
                }
            }
        }
    }
}
