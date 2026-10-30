namespace Asistente.Tests;

using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para el validador algorítmico post-IA de cargas pre-partido (KAN-408).
/// Verifica corrección automática de alucinaciones (D-1 <= 60 %, D-2 <= 75 %, D sin pesas),
/// registro de justificaciones en coachNotes y warnings, y preservación de ejercicios no afectados.
/// </summary>
public sealed class MatchDayLoadValidatorTests
{
    private readonly Guid _squatId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _benchPressId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _deadliftId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _categoryId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");

    private readonly Dictionary<Guid, string> _exerciseNames;
    private readonly MatchDayLoadValidator _validator;

    public MatchDayLoadValidatorTests()
    {
        _validator = new MatchDayLoadValidator();
        _exerciseNames = new Dictionary<Guid, string>
        {
            [_squatId] = "Sentadilla Trasera",
            [_benchPressId] = "Press de Banca Plano",
            [_deadliftId] = "Peso Muerto Convencional"
        };
    }

    [Fact]
    public void Given_DraftWithSquatAt85OnDayBeforeMatch_When_ValidateAndAdjust_Then_CapsPercentageTo60AndAddsCoachNoteAndWarning()
    {
        // Arrange: Lunes 2026-11-02, partido el sábado 2026-11-07
        // Sesión de viernes (2026-11-06 = D-1) con sentadilla al 85%
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan Pre-temporada",
            Objective: "Fuerza",
            JustificationSummary: "Periodización avanzada",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes - Piernas",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: "Enfoque neuromuscular",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 85m)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _exerciseNames);

        // Assert
        Assert.NotNull(result);
        var adjustedSession = Assert.Single(result.Content.Weeks[0].Sessions);
        var adjustedExercise = Assert.Single(adjustedSession.Exercises);

        // 1. TargetPercentage debe bajarse al 60%
        Assert.Equal(60m, adjustedExercise.TargetPercentage);

        // 2. coachNotes de la sesión debe tener la justificación
        Assert.NotNull(adjustedSession.CoachNotes);
        Assert.Contains("Ajustado por el partido del sábado 7/11: Sentadilla Trasera 85 % → 60 %", adjustedSession.CoachNotes);
        Assert.Contains("Enfoque neuromuscular", adjustedSession.CoachNotes); // Preserva notas previas

        // 3. Corrections / warnings debe incluir el aviso
        var warning = Assert.Single(result.Corrections);
        Assert.Equal("Ajustado por el partido del sábado 7/11: Sentadilla Trasera 85 % → 60 %", warning);
    }

    [Fact]
    public void Given_DraftWithBenchPressAt80TwoDaysBeforeMatch_When_ValidateAndAdjust_Then_CapsPercentageTo75AndAddsCoachNoteAndWarning()
    {
        // Arrange: Lunes 2026-11-02, partido el sábado 2026-11-07
        // Sesión de jueves (2026-11-05 = D-2) con press banca al 80%
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan Fuerza",
            Objective: "Fuerza",
            JustificationSummary: "Periodización",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Jueves - Pecho",
                            DayOfWeek: DayOfWeek.Thursday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(_benchPressId, Order: 1, Sets: 3, Reps: 8, TargetPercentage: 80m)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _exerciseNames);

        // Assert
        Assert.NotNull(result);
        var adjustedSession = Assert.Single(result.Content.Weeks[0].Sessions);
        var adjustedExercise = Assert.Single(adjustedSession.Exercises);

        // Capped to 75%
        Assert.Equal(75m, adjustedExercise.TargetPercentage);
        Assert.Equal("Ajustado por el partido del sábado 7/11: Press de Banca Plano 80 % → 75 %", adjustedSession.CoachNotes);
        Assert.Single(result.Corrections);
        Assert.Equal("Ajustado por el partido del sábado 7/11: Press de Banca Plano 80 % → 75 %", result.Corrections[0]);
    }

    [Fact]
    public void Given_DraftWithSessionOnMatchDay_When_ValidateAndAdjust_Then_RemovesSessionAndRenumbersOrderAndAddsWarning()
    {
        // Arrange: Lunes 2026-11-02, partido el sábado 2026-11-07
        // 3 sesiones: Miércoles (1), Viernes (2), Sábado (3 = día del partido D)
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan Con Sesión en Día D",
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
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _exerciseNames);

        // Assert
        Assert.NotNull(result);
        var week1 = Assert.Single(result.Content.Weeks);

        // La sesión del sábado debe haberse quitado
        Assert.Equal(2, week1.Sessions.Count);
        Assert.DoesNotContain(week1.Sessions, s => s.DayOfWeek == DayOfWeek.Saturday);

        // Las sesiones restantes deben reenumerarse en orden correlativo (1, 2)
        Assert.Equal(1, week1.Sessions[0].Order);
        Assert.Equal(DayOfWeek.Wednesday, week1.Sessions[0].DayOfWeek);
        Assert.Equal(2, week1.Sessions[1].Order);
        Assert.Equal(DayOfWeek.Sunday, week1.Sessions[1].DayOfWeek);

        // Debe registrarse el aviso
        Assert.Single(result.Corrections);
        Assert.Contains("Se quitó la sesión 'Sesión Sábado (Match Day)' (semana 1) por coincidir con el partido del sábado 7/11.", result.Corrections[0]);
    }

    [Fact]
    public void Given_DraftWithoutMatchesInRange_When_ValidateAndAdjust_Then_ContentRemainsUntouchedAndCorrectionsEmpty()
    {
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var content = new PlanDraftContentDto(
            Name: "Plan Sin Partidos",
            Objective: "Hipertrofia",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: "Notas intactas",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 4, Reps: 8, TargetPercentage: 85m)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act
        var resultNull = _validator.ValidateAndAdjust(content, startDate, []);
        var resultDifferentDate = _validator.ValidateAndAdjust(content, startDate, [new DateOnly(2027, 1, 1)]);

        // Assert
        Assert.Empty(resultNull.Corrections);
        Assert.Equal(85m, resultNull.Content.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);

        Assert.Empty(resultDifferentDate.Corrections);
        Assert.Equal(85m, resultDifferentDate.Content.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
    }

    [Fact]
    public void Given_DraftWithExerciseBelowCapOrNullPercentage_When_ValidateAndAdjust_Then_ExercisesAreUntouched()
    {
        // Arrange: Partido el sábado 2026-11-07
        // Sesión de viernes (D-1): un ejercicio al 50% (por debajo de 60%) y otro con porcentaje null (ej. movilidad)
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan Cargas Bajas",
            Objective: "Recuperación",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: "Sin cambios",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 3, Reps: 10, TargetPercentage: 50m),
                                new PlanDraftExerciseDto(_benchPressId, Order: 2, Sets: 3, Reps: 12, TargetPercentage: null)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], _exerciseNames);

        // Assert
        Assert.Empty(result.Corrections);
        var exercises = result.Content.Weeks[0].Sessions[0].Exercises;
        Assert.Equal(50m, exercises[0].TargetPercentage);
        Assert.Null(exercises[1].TargetPercentage);
        Assert.Equal("Sin cambios", result.Content.Weeks[0].Sessions[0].CoachNotes);
    }

    [Fact]
    public void Given_ExerciseNotInDictionary_When_Adjusting_Then_FallsBackToExerciseId()
    {
        // Arrange
        var unknownId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var content = new PlanDraftContentDto(
            Name: "Plan",
            Objective: "Fuerza",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(unknownId, Order: 1, Sets: 3, Reps: 8, TargetPercentage: 85m)
                            ]
                        )
                    ]
                )
            ]
        );

        // Act (exerciseNames vacío)
        var result = _validator.ValidateAndAdjust(content, startDate, [matchDate], exerciseNames: null);

        // Assert
        Assert.Single(result.Corrections);
        Assert.Contains($"ejercicio {unknownId} 85 % → 60 %", result.Corrections[0]);
    }

    [Fact]
    public async Task Given_CreatePlanDraftCommandHandler_When_AiHallucinates85PercentOnDayBeforeMatch_Then_ValidatorInterceptsAndCapsTo60BeforeSaving()
    {
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);
        var expectedEndDate = startDate.AddDays(4 * 7 - 1);

        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Fuerza",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        // La IA alucina y propone Sentadilla al 85% el viernes antes del partido
        var hallucinatedAiContent = new PlanDraftContentDto(
            Name: "Plan Generado por IA",
            Objective: "Fuerza",
            JustificationSummary: "Resumen IA",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión Viernes Pre-partido",
                            DayOfWeek: DayOfWeek.Friday,
                            Order: 1,
                            CoachNotes: "Alta intensidad",
                            Exercises:
                            [
                                new PlanDraftExerciseDto(_squatId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 85m)
                            ]
                        )
                    ]
                )
            ]
        );

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(_categoryId, startDate, expectedEndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchDate]);

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hallucinatedAiContent, "Ollama"));

        PlanDraft? savedDraft = null;
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()))
            .Callback<PlanDraft, CancellationToken>((d, _) => savedDraft = d)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogExerciseDto(_squatId, "Sentadilla Trasera", "Piernas", "Barra")]);

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarClient: calendarMock.Object,
            matchDayLoadValidator: _validator,
            catalogService: catalogMock.Object,
            logger: NullLogger<CreatePlanDraftCommandHandler>.Instance
        );

        // Act
        var response = await handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(response);

        // 1. En la respuesta devuelta al PF, el porcentaje debe estar corregido al 60%
        var session = Assert.Single(response.Weeks[0].Sessions);
        Assert.Equal(60m, session.Exercises[0].TargetPercentage);

        // 2. Response Warnings debe contener el aviso de corrección
        Assert.NotNull(response.Warnings);
        var warning = Assert.Single(response.Warnings);
        Assert.Contains("Sentadilla Trasera 85 % → 60 %", warning);

        // 3. En la entidad persistida en Postgres, el JSON ya debe estar guardado corregido
        Assert.NotNull(savedDraft);
        var persistedContent = PlanDraftMapper.DeserializeContent(savedDraft.Content);
        Assert.Equal(60m, persistedContent.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Contains("Sentadilla Trasera 85 % → 60 %", persistedContent.Weeks[0].Sessions[0].CoachNotes);
    }

    [Fact]
    public async Task Given_CreatePlanDraftCommandHandler_When_AiHallucinatesSessionOnMatchDay_Then_ValidatorRemovesSessionBeforeSaving()
    {
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);
        var expectedEndDate = startDate.AddDays(4 * 7 - 1);

        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Potencia",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        // La IA propone una sesión de fuerza en sábado (Match Day)
        var hallucinatedAiContent = new PlanDraftContentDto(
            Name: "Plan Alucinado",
            Objective: "Potencia",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto("Sesión Miércoles", DayOfWeek.Wednesday, Order: 1, null, []),
                        new PlanDraftSessionDto("Sesión Sábado (Match Day)", DayOfWeek.Saturday, Order: 2, null, [])
                    ]
                )
            ]
        );

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(_categoryId, startDate, expectedEndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchDate]);

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((hallucinatedAiContent, "Gemini"));

        PlanDraft? savedDraft = null;
        var draftRepoMock = new Mock<IPlanDraftRepository>();
        draftRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()))
            .Callback<PlanDraft, CancellationToken>((d, _) => savedDraft = d)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarClient: calendarMock.Object,
            matchDayLoadValidator: _validator,
            catalogService: null,
            logger: NullLogger<CreatePlanDraftCommandHandler>.Instance
        );

        // Act
        var response = await handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(response);

        // La sesión de sábado debe haber sido eliminada
        var week1 = Assert.Single(response.Weeks);
        var remainingSession = Assert.Single(week1.Sessions);
        Assert.Equal(DayOfWeek.Wednesday, remainingSession.DayOfWeek);
        Assert.Equal(1, remainingSession.Order);

        // Response Warnings debe incluir el aviso de remoción
        Assert.NotNull(response.Warnings);
        Assert.Single(response.Warnings);
        Assert.Contains("Se quitó la sesión 'Sesión Sábado (Match Day)'", response.Warnings[0]);

        // La entidad persistida no tiene la sesión de sábado
        Assert.NotNull(savedDraft);
        var persistedContent = PlanDraftMapper.DeserializeContent(savedDraft.Content);
        Assert.Single(persistedContent.Weeks[0].Sessions);
        Assert.Equal(DayOfWeek.Wednesday, persistedContent.Weeks[0].Sessions[0].DayOfWeek);
    }
}
