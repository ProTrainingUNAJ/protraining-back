namespace Asistente.Tests;

using Application.Common.Ai;
using Application.Common.PlanDrafts;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Pruebas automatizadas para la selección inteligente de ejercicios según objetivo y validación de reglas (#158 · KAN-563).
/// Verifica los 5 criterios de aceptación de Jira:
/// 1. Catálogo filtrado por objetivo (ej. piernas priorizadas, no alfabéticas).
/// 2. Reglas determinísticas con grupos correspondientes (piernas, empuje, tracción) y respeto de trainingDays.
/// 3. Validación estricta de weeks (1-8) y daysPerWeek (1-6) retornando 400 (DomainException) sin llamar a IA.
/// 4. Catálogo vacío sin IDs inventados retornando 503 (AiServiceUnavailableException).
/// 5. Cobertura de objetivos generales balanceando grupos musculares básicos.
/// </summary>
public sealed class PlanDraftGoalExerciseSelectionTests
{
    private static readonly Guid SquatId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid LegPressId = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly Guid BenchPressId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid OverheadPressId = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid BarbellRowId = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid PullUpId = Guid.Parse("33333333-0000-0000-0000-000000000002");
    private static readonly Guid AbsExerciseId = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid FlyesExerciseId = Guid.Parse("44444444-0000-0000-0000-000000000002");

    private readonly List<CatalogExerciseDto> _sampleCatalog =
    [
        // Ejercicios alfabéticamente primeros que NO son de piernas
        new CatalogExerciseDto(AbsExerciseId, "Abdominales en colchoneta", "Core", "Ninguno"),
        new CatalogExerciseDto(FlyesExerciseId, "Aperturas con mancuernas", "Pecho", "Mancuernas"),

        // Ejercicios de piernas
        new CatalogExerciseDto(SquatId, "Sentadilla trasera con barra", "Piernas", "Barra"),
        new CatalogExerciseDto(LegPressId, "Prensa de piernas 45°", "Piernas", "Máquina"),

        // Ejercicios de empuje
        new CatalogExerciseDto(BenchPressId, "Press de banca plano", "Pecho", "Barra"),
        new CatalogExerciseDto(OverheadPressId, "Press militar con barra", "Hombros", "Barra"),

        // Ejercicios de tracción
        new CatalogExerciseDto(BarbellRowId, "Remo con barra inclinado", "Espalda", "Barra"),
        new CatalogExerciseDto(PullUpId, "Dominadas pronas", "Espalda", "Ninguno")
    ];

    #region Criterio 1: Catálogo filtrado por objetivo (no alfabético)

    [Fact]
    public void Given_GoalLegStrength_When_FilteringExercises_Then_PrioritizesLegsOverAlphabeticalFirst()
    {
        // Arrange
        const string goal = "fuerza de piernas";

        // Act
        var filtered = RoutinePromptBuilder.FilterExercisesForGoal(_sampleCatalog, goal, maxExercises: 2);

        // Assert: Deben seleccionarse los ejercicios de piernas, no los primeros por abecedario ("Abdominales", "Aperturas")
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, e => Assert.Equal("Piernas", e.MuscleGroup));
        Assert.Contains(filtered, e => e.Id == SquatId);
        Assert.Contains(filtered, e => e.Id == LegPressId);
        Assert.DoesNotContain(filtered, e => e.Id == AbsExerciseId);
        Assert.DoesNotContain(filtered, e => e.Id == FlyesExerciseId);
    }

    [Fact]
    public void Given_GoalLegStrength_When_BuildingPlanDraftPrompt_Then_PromptContainsLegExercisesAndNotAlphabeticalOnly()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            Goal: "fuerza de piernas",
            AthleteId: Guid.NewGuid(),
            Weeks: 4,
            DaysPerWeek: 3
        );

        var filtered = RoutinePromptBuilder.FilterExercisesForGoal(_sampleCatalog, request.Goal, maxExercises: 2);

        // Act
        var prompt = RoutinePromptBuilder.BuildPlanDraftPrompt(request, filtered);

        // Assert
        Assert.Contains("Sentadilla trasera con barra", prompt);
        Assert.Contains("Prensa de piernas 45°", prompt);
        Assert.DoesNotContain("Abdominales en colchoneta", prompt);
        Assert.DoesNotContain("Aperturas con mancuernas", prompt);
    }

    #endregion

    #region Criterio 2: Reglas determinísticas con grupos y trainingDays

    [Fact]
    public async Task Given_OllamaAndGeminiDown_When_GeneratingWithRules_Then_SessionsHaveCorrespondingGroupExercisesAndRespectsTrainingDays()
    {
        // Arrange
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleCatalog);

        var auditMock = new Mock<IAiAuditService>();
        var ruleClient = new RuleBasedAiClient(auditMock.Object, Mock.Of<ILogger<RuleBasedAiClient>>(), catalogMock.Object);

        var request = new CreatePlanDraftRequestDto(
            Goal: "Planificación de pretemporada",
            AthleteId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            TrainingDays: ["Lunes", "Miércoles", "Viernes"]
        );

        // Act
        var content = await ruleClient.TryGeneratePlanDraftAsync(request);

        // Assert
        Assert.NotNull(content);
        var week = Assert.Single(content.Weeks);
        Assert.Equal(3, week.Sessions.Count);

        // Sesión 1: Lunes -> Piernas
        var session1 = week.Sessions[0];
        Assert.Equal(DayOfWeek.Monday, session1.DayOfWeek);
        Assert.Contains("Piernas", session1.Name);
        Assert.All(session1.Exercises, ex =>
        {
            var catalogItem = _sampleCatalog.First(c => c.Id == ex.ExerciseId);
            Assert.Equal("Piernas", catalogItem.MuscleGroup);
        });

        // Sesión 2: Miércoles -> Empuje
        var session2 = week.Sessions[1];
        Assert.Equal(DayOfWeek.Wednesday, session2.DayOfWeek);
        Assert.Contains("Empuje", session2.Name);
        Assert.All(session2.Exercises, ex =>
        {
            var catalogItem = _sampleCatalog.First(c => c.Id == ex.ExerciseId);
            Assert.Contains(catalogItem.MuscleGroup, new[] { "Pecho", "Hombros", "Tríceps" });
        });

        // Sesión 3: Viernes -> Tracción
        var session3 = week.Sessions[2];
        Assert.Equal(DayOfWeek.Friday, session3.DayOfWeek);
        Assert.Contains("Tracción", session3.Name);
        Assert.All(session3.Exercises, ex =>
        {
            var catalogItem = _sampleCatalog.First(c => c.Id == ex.ExerciseId);
            Assert.Equal("Espalda", catalogItem.MuscleGroup);
        });
    }

    [Fact]
    public async Task Given_CustomTrainingDays_When_GeneratingWithRules_Then_StrictlyFollowsCustomDays()
    {
        // Arrange
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sampleCatalog);

        var ruleClient = new RuleBasedAiClient(Mock.Of<IAiAuditService>(), Mock.Of<ILogger<RuleBasedAiClient>>(), catalogMock.Object);

        var request = new CreatePlanDraftRequestDto(
            Goal: "Mantenimiento",
            AthleteId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 2,
            TrainingDays: ["Martes", "Jueves"]
        );

        // Act
        var content = await ruleClient.TryGeneratePlanDraftAsync(request);

        // Assert
        Assert.NotNull(content);
        var week = Assert.Single(content.Weeks);
        Assert.Equal(2, week.Sessions.Count);
        Assert.Equal(DayOfWeek.Tuesday, week.Sessions[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Thursday, week.Sessions[1].DayOfWeek);
    }

    #endregion

    #region Criterio 3: Validación estricta semanas (1..8) y días/semana (1..6) retorna 400 sin llamar IA

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(20)]
    public async Task Given_WeeksOutOfRange_When_CreatingPlanDraft_Then_ThrowsDomainExceptionAndDoesNotCallAi(int invalidWeeks)
    {
        // Arrange
        var aiServiceMock = new Mock<IAiService>();
        var repoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            repoMock.Object,
            uowMock.Object);

        var command = new CreatePlanDraftCommand(new CreatePlanDraftRequestDto(
            Goal: "Objetivo válido",
            AthleteId: Guid.NewGuid(),
            Weeks: invalidWeeks,
            DaysPerWeek: 3
        ));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.Contains("La cantidad de semanas debe estar entre 1 y 8.", ex.Message);

        // Verifica que NUNCA se llame a la IA
        aiServiceMock.Verify(
            a => a.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(10)]
    public async Task Given_DaysPerWeekOutOfRange_When_CreatingPlanDraft_Then_ThrowsDomainExceptionAndDoesNotCallAi(int invalidDays)
    {
        // Arrange
        var aiServiceMock = new Mock<IAiService>();
        var repoMock = new Mock<IPlanDraftRepository>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new CreatePlanDraftCommandHandler(
            aiServiceMock.Object,
            repoMock.Object,
            uowMock.Object);

        var command = new CreatePlanDraftCommand(new CreatePlanDraftRequestDto(
            Goal: "Objetivo válido",
            AthleteId: Guid.NewGuid(),
            Weeks: 4,
            DaysPerWeek: invalidDays
        ));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => handler.HandleAsync(command));
        Assert.Contains("La cantidad de días por semana debe estar entre 1 y 6.", ex.Message);

        // Verifica que NUNCA se llame a la IA
        aiServiceMock.Verify(
            a => a.GeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Criterio 4: Catálogo vacío sin IDs inventados lanza 503

    [Fact]
    public async Task Given_EmptyCatalog_When_GeneratingPlanDraftWithRuleClient_Then_ReturnsNullAndNoInventedIds()
    {
        // Arrange
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogExerciseDto>());

        var ruleClient = new RuleBasedAiClient(Mock.Of<IAiAuditService>(), Mock.Of<ILogger<RuleBasedAiClient>>(), catalogMock.Object);

        var request = new CreatePlanDraftRequestDto(
            Goal: "Fuerza general",
            AthleteId: Guid.NewGuid(),
            Weeks: 2,
            DaysPerWeek: 3
        );

        // Act
        var content = await ruleClient.TryGeneratePlanDraftAsync(request);

        // Assert: Debe ser nulo, NO retornar IDs ficticios hardcodeados (11111111-..., 22222222-..., etc.)
        Assert.Null(content);
    }

    [Fact]
    public async Task Given_EmptyCatalog_When_GeneratingPlanDraftWithResilientAiService_Then_ThrowsAiServiceUnavailableException503()
    {
        // Arrange
        var catalogMock = new Mock<IExerciseCatalogService>();
        catalogMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogExerciseDto>());

        var ruleClient = new RuleBasedAiClient(Mock.Of<IAiAuditService>(), Mock.Of<ILogger<RuleBasedAiClient>>(), catalogMock.Object);

        // Mock IA Providers fallando / retornando null
        var ollamaMock = new Mock<IAiClient>();
        ollamaMock.Setup(o => o.ProviderName).Returns("Ollama");
        ollamaMock
            .Setup(o => o.TryGeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanDraftContentDto?)null);

        var geminiMock = new Mock<IAiClient>();
        geminiMock.Setup(g => g.ProviderName).Returns("Gemini");
        geminiMock
            .Setup(g => g.TryGeneratePlanDraftAsync(It.IsAny<CreatePlanDraftRequestDto>(), It.IsAny<IReadOnlyList<DateOnly>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanDraftContentDto?)null);

        var resilientService = new ResilientAiService(
            providers: [ollamaMock.Object, geminiMock.Object, ruleClient],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: catalogMock.Object,
            logger: Mock.Of<ILogger<ResilientAiService>>());

        var request = new CreatePlanDraftRequestDto(
            Goal: "Fuerza general",
            AthleteId: Guid.NewGuid(),
            Weeks: 2,
            DaysPerWeek: 3
        );

        // Act & Assert: Debe arrojar AiServiceUnavailableException (que ExceptionMiddleware mapea a HTTP 503)
        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() =>
            resilientService.GeneratePlanDraftAsync(request));

        Assert.Contains("El servicio de generación de planificaciones con inteligencia artificial no se encuentra disponible", ex.Message);
    }

    #endregion

    #region Criterio 5: Objetivo general distribuye ejercicios balanceados

    [Fact]
    public void Given_GeneralGoal_When_FilteringExercises_Then_BalancesAcrossDifferentMuscleGroups()
    {
        // Arrange
        const string goal = "pretemporada general de fuerza y acondicionamiento";

        // Act
        var filtered = RoutinePromptBuilder.FilterExercisesForGoal(_sampleCatalog, goal, maxExercises: 6);

        // Assert
        Assert.True(filtered.Count >= 3);
        var muscleGroups = filtered.Select(f => f.MuscleGroup).Distinct().ToList();
        Assert.True(muscleGroups.Count >= 3, "Debe contener al menos 3 grupos musculares distintos para un objetivo general");
    }

    #endregion
}

