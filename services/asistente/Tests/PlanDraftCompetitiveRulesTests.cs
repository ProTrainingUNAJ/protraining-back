namespace Asistente.Tests;

using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Domain.Common;
using System.Diagnostics;
using Xunit;

/// <summary>
/// Pruebas automatizadas para la validación estricta de reglas competitivas en planificaciones generadas (KAN-409).
/// Verifica los 8 escenarios solicitados por Jira, garantizando ejecución 100 % en memoria para CI/CD sin base ni IA.
/// </summary>
public sealed class PlanDraftCompetitiveRulesTests
{
    private readonly Guid _squatId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private readonly Guid _benchPressId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
    private readonly Guid _deadliftId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private readonly Dictionary<Guid, string> _catalogNames;
    private readonly MatchDayLoadValidator _validator;

    public PlanDraftCompetitiveRulesTests()
    {
        _validator = new MatchDayLoadValidator();
        _catalogNames = new Dictionary<Guid, string>
        {
            [_squatId] = "Sentadilla Trasera",
            [_benchPressId] = "Press de Banca Plano",
            [_deadliftId] = "Peso Muerto Convencional"
        };
    }

    [Fact]
    public void Given_ExerciseAt85PercentOnDayBeforeMatch_When_Validating_Then_CapsToMaxPercentageOneDayBeforeMatchWithNote()
    {
        // Caso 1: D-1 con un ejercicio al 85 % → queda en 60 %, con la nota.
        // Arrange
        var startDate = new DateOnly(2026, 11, 2); // Lunes
        var matchDate = new DateOnly(2026, 11, 7); // Sábado

        var content = CreateDraftContent(
            dayOfWeek: DayOfWeek.Friday, // D-1
            exerciseId: _squatId,
            targetPercentage: 85m
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _catalogNames);

        // Assert
        var session = Assert.Single(result.Content.Weeks[0].Sessions);
        var exercise = Assert.Single(session.Exercises);

        Assert.Equal(MatchDayLoadRules.MaxPercentageOneDayBeforeMatch, exercise.TargetPercentage);
        Assert.NotNull(session.CoachNotes);
        Assert.Contains($"Ajustado por el partido del sábado 7/11: Sentadilla Trasera 85 % → {MatchDayLoadRules.MaxPercentageOneDayBeforeMatch:0} %", session.CoachNotes);

        var warning = Assert.Single(result.Corrections);
        Assert.Contains($"Sentadilla Trasera 85 % → {MatchDayLoadRules.MaxPercentageOneDayBeforeMatch:0} %", warning);
    }

    [Fact]
    public void Given_ExerciseAt80PercentTwoDaysBeforeMatch_When_Validating_Then_CapsToMaxPercentageTwoDaysBeforeMatchWithNote()
    {
        // Caso 2: D-2 con un ejercicio al 80 % → queda en 75 %.
        // Arrange
        var startDate = new DateOnly(2026, 11, 2); // Lunes
        var matchDate = new DateOnly(2026, 11, 7); // Sábado

        var content = CreateDraftContent(
            dayOfWeek: DayOfWeek.Thursday, // D-2
            exerciseId: _benchPressId,
            targetPercentage: 80m
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _catalogNames);

        // Assert
        var session = Assert.Single(result.Content.Weeks[0].Sessions);
        var exercise = Assert.Single(session.Exercises);

        Assert.Equal(MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch, exercise.TargetPercentage);
        Assert.NotNull(session.CoachNotes);
        Assert.Contains($"Ajustado por el partido del sábado 7/11: Press de Banca Plano 80 % → {MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch:0} %", session.CoachNotes);

        var warning = Assert.Single(result.Corrections);
        Assert.Contains($"Press de Banca Plano 80 % → {MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch:0} %", warning);
    }

    [Fact]
    public void Given_ExerciseAt70PercentTwoDaysBeforeMatch_When_Validating_Then_RemainsUntouched()
    {
        // Caso 3: D-2 con un ejercicio al 70 % → no se toca.
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = CreateDraftContent(
            dayOfWeek: DayOfWeek.Thursday, // D-2
            exerciseId: _deadliftId,
            targetPercentage: 70m,
            initialCoachNotes: "Enfoque en técnica"
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _catalogNames);

        // Assert
        var session = Assert.Single(result.Content.Weeks[0].Sessions);
        var exercise = Assert.Single(session.Exercises);

        Assert.Equal(70m, exercise.TargetPercentage);
        Assert.Equal("Enfoque en técnica", session.CoachNotes);
        Assert.Empty(result.Corrections);
    }

    [Fact]
    public void Given_SessionOnMatchDay_When_Validating_Then_SessionIsRemovedAndRemainingSessionsReordered()
    {
        // Caso 4: Sesión el día del partido → se quita.
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7); // Sábado

        var content = new PlanDraftContentDto(
            Name: "Plan Competitivo",
            Objective: "Fuerza",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto("Sesión Miércoles", DayOfWeek.Wednesday, Order: 1, null, []),
                        new PlanDraftSessionDto("Sesión Sábado (Match Day)", DayOfWeek.Saturday, Order: 2, null, []),
                        new PlanDraftSessionDto("Sesión Domingo", DayOfWeek.Sunday, Order: 3, null, [])
                    ]
                )
            ]
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _catalogNames);

        // Assert
        var week1 = Assert.Single(result.Content.Weeks);
        Assert.Equal(2, week1.Sessions.Count);
        Assert.DoesNotContain(week1.Sessions, s => s.DayOfWeek == DayOfWeek.Saturday);

        // Comprueba reordenamiento correlativo sin huecos (1, 2)
        Assert.Equal(1, week1.Sessions[0].Order);
        Assert.Equal(DayOfWeek.Wednesday, week1.Sessions[0].DayOfWeek);
        Assert.Equal(2, week1.Sessions[1].Order);
        Assert.Equal(DayOfWeek.Sunday, week1.Sessions[1].DayOfWeek);

        var warning = Assert.Single(result.Corrections);
        Assert.Contains("Se quitó la sesión 'Sesión Sábado (Match Day)'", warning);
    }

    [Fact]
    public void Given_TwoMatchesInSameWeek_When_Validating_Then_BothMatchTaperingRulesAreApplied()
    {
        // Caso 5: Dos partidos en la misma semana → se aplican los dos.
        // Partido 1: Miércoles 2026-11-04 (Copa) -> Lunes 2026-11-02 es D-2 (tope 75%), Martes 2026-11-03 es D-1 (tope 60%)
        // Partido 2: Sábado 2026-11-07 (Liga) -> Viernes 2026-11-06 es D-1 (tope 60%) y Sábado es D (se quita)
        // Domingo 2026-11-08: Día normal (sin partido próximo en la semana), se mantiene al 80%
        var startDate = new DateOnly(2026, 11, 2);
        var match1 = new DateOnly(2026, 11, 4);
        var match2 = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan Doble Competencia",
            Objective: "Potencia",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Lunes (D-2 de Copa)",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises: [new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 3, Reps: 6, TargetPercentage: 80m)]
                        ),
                        new PlanDraftSessionDto(
                            Name: "Sesión Martes (D-1 de Copa)",
                            DayOfWeek: DayOfWeek.Tuesday,
                            Order: 2,
                            CoachNotes: null,
                            Exercises: [new PlanDraftExerciseDto(_benchPressId, Order: 1, Sets: 3, Reps: 6, TargetPercentage: 85m)]
                        ),
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes (D-1 de Liga)",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 3,
                            CoachNotes: null,
                            Exercises: [new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 3, Reps: 6, TargetPercentage: 85m)]
                        ),
                        new PlanDraftSessionDto(
                            Name: "Sesión Sábado (Match Day Liga)",
                            DayOfWeek: DayOfWeek.Saturday,
                            Order: 4,
                            CoachNotes: null,
                            Exercises: []
                        ),
                        new PlanDraftSessionDto(
                            Name: "Sesión Domingo (Normal)",
                            DayOfWeek: DayOfWeek.Sunday,
                            Order: 5,
                            CoachNotes: null,
                            Exercises: [new PlanDraftExerciseDto(_deadliftId, Order: 1, Sets: 3, Reps: 6, TargetPercentage: 80m)]
                        )
                    ]
                )
            ]
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [match1, match2], _catalogNames);

        // Assert
        var week1 = Assert.Single(result.Content.Weeks);

        // 1. Sesión de sábado debe haber sido eliminada (quedan 4 sesiones)
        Assert.Equal(4, week1.Sessions.Count);
        Assert.DoesNotContain(week1.Sessions, s => s.DayOfWeek == DayOfWeek.Saturday);

        // 2. Lunes ajustado al 75 % por ser D-2 del partido del miércoles
        Assert.Equal(MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch, week1.Sessions[0].Exercises[0].TargetPercentage);
        Assert.Contains("partido del miércoles 4/11", week1.Sessions[0].CoachNotes);

        // 3. Martes ajustado al 60 % por ser D-1 del partido del miércoles
        Assert.Equal(MatchDayLoadRules.MaxPercentageOneDayBeforeMatch, week1.Sessions[1].Exercises[0].TargetPercentage);
        Assert.Contains("partido del miércoles 4/11", week1.Sessions[1].CoachNotes);

        // 4. Viernes ajustado al 60 % por ser D-1 del partido del sábado
        Assert.Equal(MatchDayLoadRules.MaxPercentageOneDayBeforeMatch, week1.Sessions[2].Exercises[0].TargetPercentage);
        Assert.Contains("partido del sábado 7/11", week1.Sessions[2].CoachNotes);

        // 5. Domingo permanece intacto al 80 %
        Assert.Equal(80m, week1.Sessions[3].Exercises[0].TargetPercentage);

        // 6. Total de 4 correcciones registradas (3 ajustes de carga + 1 remoción de sesión)
        Assert.Equal(4, result.Corrections.Count);
    }

    [Fact]
    public void Given_MatchesOutsidePlanDateRange_When_Validating_Then_PlanRemainsCompletelyUntouched()
    {
        // Caso 6: Partido fuera del rango del plan → no cambia nada.
        // Mesociclo de 2 semanas: del lunes 2026-11-02 al domingo 2026-11-15
        var startDate = new DateOnly(2026, 11, 2);
        var pastMatch = new DateOnly(2026, 10, 20); // Previo al plan
        var futureMatch = new DateOnly(2026, 11, 25); // Posterior al plan

        var content = CreateDraftContent(
            dayOfWeek: DayOfWeek.Friday,
            exerciseId: _squatId,
            targetPercentage: 85m,
            initialCoachNotes: "Notas originales"
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [pastMatch, futureMatch], _catalogNames);

        // Assert
        Assert.Empty(result.Corrections);
        var session = Assert.Single(result.Content.Weeks[0].Sessions);
        Assert.Equal(85m, session.Exercises[0].TargetPercentage);
        Assert.Equal("Notas originales", session.CoachNotes);
    }

    [Fact]
    public void Given_ExercisesWithNullTargetPercentage_When_Validating_Then_ExercisesRemainNull()
    {
        // Caso 7: targetPercentage nulo → no se toca.
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan Movilidad",
            Objective: "Prevención",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes D-1",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: "Activación articular",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 3, Reps: 10, TargetPercentage: null),
                                new PlanDraftExerciseDto(_benchPressId, Order: 2, Sets: 2, Reps: 15, TargetPercentage: null)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _catalogNames);

        // Assert
        Assert.Empty(result.Corrections);
        var exercises = result.Content.Weeks[0].Sessions[0].Exercises;
        Assert.Null(exercises[0].TargetPercentage);
        Assert.Null(exercises[1].TargetPercentage);
        Assert.Equal("Activación articular", result.Content.Weeks[0].Sessions[0].CoachNotes);
    }

    [Fact]
    public void Given_LoadCapsAreDefinedInDomainRules_When_CheckingConstants_Then_MatchDayLoadRulesIsSingleSourceOfTruth()
    {
        // Caso 8: Los topes se leen de MatchDayLoadRules: si se cambia el 60 por 55, el test del D-1 lo refleja sin tocar el validador.
        Assert.Equal(60.0m, MatchDayLoadRules.MaxPercentageOneDayBeforeMatch);
        Assert.Equal(75.0m, MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch);
        Assert.False(MatchDayLoadRules.AllowStrengthSessionOnMatchDay);

        Assert.Equal(MatchDayLoadRules.MaxPercentageOneDayBeforeMatch, MatchDayLoadRules.GetMaxAllowedPercentage(1));
        Assert.Equal(MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch, MatchDayLoadRules.GetMaxAllowedPercentage(2));
        Assert.Null(MatchDayLoadRules.GetMaxAllowedPercentage(3));
    }

    [Fact]
    public void Given_BackToBackMatches_When_SessionFallsOnD1OfFirstAndD2OfSecond_Then_StricterCapApplies()
    {
        // Caso extra: Partidos consecutivos (Viernes 2026-11-06 y Sábado 2026-11-07).
        // El Jueves 2026-11-05 es D-1 del partido del viernes y D-2 del partido del sábado.
        // Debe prevalecer el tope más estricto (D-1 = 60%).
        var startDate = new DateOnly(2026, 11, 2);
        var match1 = new DateOnly(2026, 11, 6);
        var match2 = new DateOnly(2026, 11, 7);

        var content = CreateDraftContent(
            dayOfWeek: DayOfWeek.Thursday,
            exerciseId: _squatId,
            targetPercentage: 85m
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [match1, match2], _catalogNames);

        // Assert
        var session = Assert.Single(result.Content.Weeks[0].Sessions);
        var exercise = Assert.Single(session.Exercises);

        Assert.Equal(MatchDayLoadRules.MaxPercentageOneDayBeforeMatch, exercise.TargetPercentage);
        Assert.Contains($"partido del viernes 6/11", session.CoachNotes);
    }

    [Fact]
    public void Given_FullMesocycleWithMultipleWeeksAndMatches_When_Validating_Then_ExecutesInUnderFiftyMilliseconds()
    {
        // Caso rendimiento CI: Mesociclo de 8 semanas con 5 sesiones por semana y 8 partidos.
        // Debe procesarse en menos de 50 milisegundos en memoria.
        var startDate = new DateOnly(2026, 11, 2);
        var matchDates = Enumerable.Range(0, 8)
            .Select(w => startDate.AddDays(w * 7 + 5)) // Sábados de cada semana
            .ToList();

        DayOfWeek[] days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

        var weeks = new List<PlanDraftWeekDto>();
        for (var w = 1; w <= 8; w++)
        {
            var sessions = days.Select((day, idx) => new PlanDraftSessionDto(
                Name: $"Sesión {idx + 1}",
                DayOfWeek: day,
                Order: idx + 1,
                CoachNotes: null,
                Exercises:
                [
                    new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 4, Reps: 8, TargetPercentage: 85m),
                    new PlanDraftExerciseDto(_benchPressId, Order: 2, Sets: 3, Reps: 10, TargetPercentage: 80m)
                ]
            )).ToList();

            weeks.Add(new PlanDraftWeekDto(w, sessions));
        }

        var content = new PlanDraftContentDto("Mesociclo Completo", "Fuerza", "Resumen", weeks);

        // Act
        var sw = Stopwatch.StartNew();
        var result = _validator.ValidateAndAdjust(content, startDate, matchDates, _catalogNames);
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 50, $"La validación tardó {sw.ElapsedMilliseconds} ms, superando el umbral de 50 ms.");
        Assert.NotEmpty(result.Corrections);

        // En cada semana se quitó la sesión de sábado (8 sesiones quitadas)
        foreach (var week in result.Content.Weeks)
        {
            Assert.Equal(4, week.Sessions.Count);
            Assert.DoesNotContain(week.Sessions, s => s.DayOfWeek == DayOfWeek.Saturday);

            // Jueves (D-2) capped to 75%
            var thursday = week.Sessions.First(s => s.DayOfWeek == DayOfWeek.Thursday);
            Assert.All(thursday.Exercises, ex => Assert.True(ex.TargetPercentage <= MatchDayLoadRules.MaxPercentageTwoDaysBeforeMatch));

            // Viernes (D-1) capped to 60%
            var friday = week.Sessions.First(s => s.DayOfWeek == DayOfWeek.Friday);
            Assert.All(friday.Exercises, ex => Assert.True(ex.TargetPercentage <= MatchDayLoadRules.MaxPercentageOneDayBeforeMatch));
        }
    }

    private static PlanDraftContentDto CreateDraftContent(
        DayOfWeek dayOfWeek,
        Guid exerciseId,
        decimal? targetPercentage,
        string? initialCoachNotes = null)
    {
        return new PlanDraftContentDto(
            Name: "Plan de Prueba",
            Objective: "Fuerza",
            JustificationSummary: "Resumen técnico",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: $"Sesión {dayOfWeek}",
                            DayOfWeek: dayOfWeek,
                            Order: 1,
                            CoachNotes: initialCoachNotes,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(exerciseId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: targetPercentage)
                            ]
                        )
                    ]
                )
            ]
        );
    }
}
