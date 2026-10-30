namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Domain.Entities;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para la inyección de directivas de periodización competitiva pre-partido (KAN-407).
/// </summary>
public sealed class PlanDraftMatchPeriodizationPromptTests
{
    private readonly Guid _categoryId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private readonly Guid _athleteId = Guid.Parse("22222222-3333-4444-5555-666666666666");

    private readonly List<CatalogExerciseDto> _sampleExercises =
    [
        new(Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444"), "Sentadilla Trasera", "Piernas", "Barra"),
        new(Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444"), "Press Banca", "Pecho", "Barra")
    ];

    [Fact]
    public void Given_CategoryWithMatchOnSaturday_When_BuildingPrompt_Then_ContainsMatchDatesAndTaperingRules()
    {
        // Arrange: Mesociclo de 4 semanas arrancando lunes 2026-11-02, partido el sábado 2026-11-07
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Fuerza Máxima y Potencia",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, _sampleExercises, [matchDate]);

        // Assert
        Assert.Contains("COMPETITIVE MATCH SCHEDULE (CALENDAR):", prompt);
        Assert.Contains("- Week 1, Saturday (2026-11-07): Official Match", prompt);
        Assert.Contains("MANDATORY MATCHDAY PERIODIZATION AND TAPERING RULES:", prompt);
        Assert.Contains("1. MATCH DAY (Day D): Do NOT schedule any strength or gym sessions on the day of the match.", prompt);
        Assert.Contains("2. DAY BEFORE MATCH (Day D-1): Target percentage MUST NOT exceed 60% for any exercise", prompt);
        Assert.Contains("3. TWO DAYS BEFORE MATCH (Day D-2): Target percentage MUST NOT exceed 75% for any exercise", prompt);
        Assert.Contains("4. Rest of the days: Normal training intensities according to the goal.", prompt);
    }

    [Fact]
    public void Given_RequestWithoutMatches_When_BuildingPrompt_Then_DoesNotContainMatchDirectives()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Hipertrofia",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 2)
        );

        // Act
        var promptNull = RoutinePromptBuilder.BuildPlanDraftPrompt(request, _sampleExercises, null);
        var promptEmpty = RoutinePromptBuilder.BuildPlanDraftPrompt(request, _sampleExercises, []);

        // Assert
        Assert.DoesNotContain("COMPETITIVE MATCH SCHEDULE (CALENDAR):", promptNull);
        Assert.DoesNotContain("MANDATORY MATCHDAY PERIODIZATION AND TAPERING RULES:", promptNull);

        Assert.DoesNotContain("COMPETITIVE MATCH SCHEDULE (CALENDAR):", promptEmpty);
        Assert.DoesNotContain("MANDATORY MATCHDAY PERIODIZATION AND TAPERING RULES:", promptEmpty);
    }

    [Fact]
    public void Given_MatchesOutsideDateRange_When_BuildingPrompt_Then_FiltersThemOut()
    {
        // Arrange: 2 semanas (del 2026-11-02 al 2026-11-15)
        var startDate = new DateOnly(2026, 11, 2);
        var pastMatch = new DateOnly(2026, 10, 25);
        var futureMatch = new DateOnly(2026, 11, 20);

        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Resistencia",
            Weeks: 2,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, _sampleExercises, [pastMatch, futureMatch]);

        // Assert
        Assert.DoesNotContain("COMPETITIVE MATCH SCHEDULE (CALENDAR):", prompt);
        Assert.DoesNotContain("MANDATORY MATCHDAY PERIODIZATION AND TAPERING RULES:", prompt);
    }

    [Fact]
    public async Task Given_CategoryWithMatchOnSaturday_When_GeneratedByRulesEngine_Then_FridayCappedAt60AndThursdayCappedAt75AndSaturdaySkipped()
    {
        // Arrange: Semana 1 (lunes 2026-11-02 al domingo 2026-11-08)
        // 6 días/semana (lunes, martes, miércoles, jueves, viernes, sábado)
        // Partido oficial: sábado 2026-11-07
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);

        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Fuerza y Potencia",
            Weeks: 1,
            DaysPerWeek: 6,
            StartDate: startDate
        );

        var auditMock = new Mock<IAiAuditService>();
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleExercises);

        var client = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance, catalogMock.Object);

        // Act
        var content = await client.TryGeneratePlanDraftAsync(request, [matchDate], CancellationToken.None);

        // Assert
        Assert.NotNull(content);
        var week1 = Assert.Single(content.Weeks);

        // Sábado (D) NO debe tener sesión de pesas
        Assert.DoesNotContain(week1.Sessions, s => s.DayOfWeek == DayOfWeek.Saturday);

        // Viernes (D-1): Capped <= 60%
        var fridaySession = week1.Sessions.FirstOrDefault(s => s.DayOfWeek == DayOfWeek.Friday);
        Assert.NotNull(fridaySession);
        Assert.All(fridaySession.Exercises, ex => Assert.Equal(60m, ex.TargetPercentage));
        Assert.Contains("D-1", fridaySession.CoachNotes);

        // Jueves (D-2): Capped <= 75%
        var thursdaySession = week1.Sessions.FirstOrDefault(s => s.DayOfWeek == DayOfWeek.Thursday);
        Assert.NotNull(thursdaySession);
        Assert.All(thursdaySession.Exercises, ex => Assert.Equal(75m, ex.TargetPercentage));
        Assert.Contains("D-2", thursdaySession.CoachNotes);

        // Lunes (Normal): 80%
        var mondaySession = week1.Sessions.FirstOrDefault(s => s.DayOfWeek == DayOfWeek.Monday);
        Assert.NotNull(mondaySession);
        Assert.All(mondaySession.Exercises, ex => Assert.Equal(80m, ex.TargetPercentage));
    }

    [Fact]
    public async Task Given_CreatePlanDraftCommandHandler_When_CategoryProvided_Then_QueriesStructureClientForMatches()
    {
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var matchDate = new DateOnly(2026, 11, 7);
        var expectedEndDate = startDate.AddDays(4 * 7 - 1); // 2026-11-29

        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Fuerza",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(_categoryId, startDate, expectedEndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchDate]);

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(
                It.Is<CreatePlanDraftRequestDto>(r => r.CategoryId == _categoryId),
                It.Is<IReadOnlyList<DateOnly>?>(matches => matches != null && matches.Contains(matchDate)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PlanDraftContentDto("Plan Test", "Fuerza", "Resumen", []), "Reglas"));

        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarMock.Object
        );

        // Act
        var result = await handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        calendarMock.Verify(c => c.GetMatchDatesAsync(_categoryId, startDate, expectedEndDate, It.IsAny<CancellationToken>()), Times.Once);
        aiServiceMock.Verify(s => s.GeneratePlanDraftAsync(
            It.IsAny<CreatePlanDraftRequestDto>(),
            It.Is<IReadOnlyList<DateOnly>?>(m => m != null && m.Contains(matchDate)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_CreatePlanDraftCommandHandler_When_CalendarClientThrows_Then_ContinuesGracefullyWithoutThrowing()
    {
        // Arrange
        var startDate = new DateOnly(2026, 11, 2);
        var request = new CreatePlanDraftRequestDto(
            CategoryId: _categoryId,
            Goal: "Fuerza",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: startDate
        );

        var calendarMock = new Mock<IStructureCalendarClient>();
        calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Estructura service unavailable"));

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(
                It.IsAny<CreatePlanDraftRequestDto>(),
                It.Is<IReadOnlyList<DateOnly>?>(m => m == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PlanDraftContentDto("Plan Test", "Fuerza", "Resumen", []), "Reglas"));

        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarMock.Object
        );

        // Act & Assert (debe degradar graciosamente y no arrojar HttpRequestException)
        var result = await handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None);
        Assert.NotNull(result);
        aiServiceMock.Verify(s => s.GeneratePlanDraftAsync(
            It.IsAny<CreatePlanDraftRequestDto>(),
            It.Is<IReadOnlyList<DateOnly>?>(m => m == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_CreatePlanDraftCommandHandler_When_AthleteProvided_Then_DoesNotQueryCalendarClient()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: _athleteId,
            Goal: "Fuerza individual",
            Weeks: 4,
            DaysPerWeek: 3
        );

        var calendarMock = new Mock<IStructureCalendarClient>();

        var aiServiceMock = new Mock<IAiService>();
        aiServiceMock
            .Setup(s => s.GeneratePlanDraftAsync(
                It.IsAny<CreatePlanDraftRequestDto>(),
                It.Is<IReadOnlyList<DateOnly>?>(m => m == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PlanDraftContentDto("Plan Individual", "Fuerza", "Resumen", []), "Reglas"));

        var draftRepoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            draftRepoMock.Object,
            uowMock.Object,
            calendarMock.Object
        );

        // Act
        var result = await handler.HandleAsync(new CreatePlanDraftCommand(request), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        calendarMock.Verify(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

