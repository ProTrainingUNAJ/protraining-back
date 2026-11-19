namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.DTOs.PlanDrafts;
using Infrastructure.Services.Ai.Agent;
using Infrastructure.Services.Ai.Gemini;
using Xunit;

/// <summary>
/// Suite de pruebas unitarias para la evaluación y optimización de prompts con criterio deportivo.
/// Valida la especificidad muscular, coherencia de bloques, tiempos de pausa e hiperparámetros de IA.
/// </summary>
public sealed class SportPromptEvaluationTests
{
    private static IReadOnlyList<CatalogExerciseDto> CreateComprehensiveCatalog()
    {
        return
        [
            // Piernas
            new CatalogExerciseDto(Guid.NewGuid(), "Sentadilla trasera con barra", "Piernas", "Barra"),
            new CatalogExerciseDto(Guid.NewGuid(), "Prensa de piernas 45°", "Piernas", "Máquina"),
            new CatalogExerciseDto(Guid.NewGuid(), "Peso muerto rumano", "Piernas", "Barra"),
            new CatalogExerciseDto(Guid.NewGuid(), "Estocadas caminando", "Piernas", "Mancuernas"),
            new CatalogExerciseDto(Guid.NewGuid(), "Movilidad de cadera y tobillo", "Piernas", "Corporal"),

            // Pecho / Empuje
            new CatalogExerciseDto(Guid.NewGuid(), "Press de banca plano", "Pecho", "Barra"),
            new CatalogExerciseDto(Guid.NewGuid(), "Press inclinado con mancuernas", "Pecho", "Mancuernas"),
            new CatalogExerciseDto(Guid.NewGuid(), "Fondos en paralelas", "Pecho", "Corporal"),

            // Espalda / Tracción
            new CatalogExerciseDto(Guid.NewGuid(), "Remo con barra", "Espalda", "Barra"),
            new CatalogExerciseDto(Guid.NewGuid(), "Dominadas pronas", "Espalda", "Corporal"),
            new CatalogExerciseDto(Guid.NewGuid(), "Jalón al pecho en polea", "Espalda", "Polea"),

            // Hombros
            new CatalogExerciseDto(Guid.NewGuid(), "Press militar con barra", "Hombros", "Barra"),
            new CatalogExerciseDto(Guid.NewGuid(), "Vuelos laterales con mancuernas", "Hombros", "Mancuernas"),

            // Brazos
            new CatalogExerciseDto(Guid.NewGuid(), "Curl de bíceps con barra", "Brazos", "Barra"),
            new CatalogExerciseDto(Guid.NewGuid(), "Extensión de tríceps en polea", "Brazos", "Polea"),

            // Core / Zona media
            new CatalogExerciseDto(Guid.NewGuid(), "Plancha frontal isométrica", "Core", "Corporal"),
            new CatalogExerciseDto(Guid.NewGuid(), "Rueda abdominal", "Core", "Rueda"),
            new CatalogExerciseDto(Guid.NewGuid(), "Press Pallof con banda", "Core", "Banda")
        ];
    }

    [Fact]
    public void Given_UpperBodyStrengthGoal_When_BuildingBlockPrompt_Then_EnforcesUpperBodyAndExcludesLegsFromMainDirectives()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        var request = new CreatePlanDraftRequestDto(
            Goal: "fuerza de tren superior",
            AthleteId: Guid.NewGuid(),
            Weeks: 4,
            DaysPerWeek: 3
        );

        var filteredExercises = RoutinePromptBuilder.FilterExercisesForGoal(catalog, request.Goal, maxExercises: 8);

        // Act
        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, filteredExercises);

        // Assert
        Assert.Contains("ENFOQUE MUSCULAR DEL OBJETIVO", prompt);
        Assert.Contains("Pecho", prompt);
        Assert.Contains("Espalda", prompt);
        Assert.Contains("DIRECTIVAS DE ESPECIFICIDAD DEPORTIVA", prompt);
        Assert.Contains("no utilices sentadillas ni prensa en los bloques principales", prompt);

        // Los ejercicios filtrados para el prompt no deben contener ejercicios de piernas
        Assert.All(filteredExercises, ex => Assert.NotEqual("Piernas", ex.MuscleGroup));
    }

    [Fact]
    public void Given_RugbyPowerGoal_When_BuildingBlockPrompt_Then_IdentifiesLegsBackAndCoreFocus()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        var request = new CreatePlanDraftRequestDto(
            Goal: "potencia y choque para rugby",
            AthleteId: Guid.NewGuid(),
            Weeks: 4,
            DaysPerWeek: 4
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, catalog);
        var detectedGroups = RoutinePromptBuilder.ExtractMuscleGroups(request.Goal);

        // Assert
        Assert.Contains("Piernas", detectedGroups);
        Assert.Contains("Espalda", detectedGroups);
        Assert.Contains("Core", detectedGroups);
        Assert.Contains("ENFOQUE MUSCULAR DEL OBJETIVO: Piernas, Espalda, Core", prompt);
    }

    [Fact]
    public void Given_BackAndTractionGoal_When_BuildingBlockPrompt_Then_PrioritizesTractionAndAccessoryRanges()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        var request = new CreatePlanDraftRequestDto(
            Goal: "hipertrofia de espalda y tracción",
            AthleteId: Guid.NewGuid(),
            Weeks: 3,
            DaysPerWeek: 3
        );

        var filtered = RoutinePromptBuilder.FilterExercisesForGoal(catalog, request.Goal, maxExercises: 5);

        // Act
        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, filtered);

        // Assert
        Assert.Contains("Espalda", prompt);
        Assert.Contains("accessories: 1-3 ejercicios analíticos, de aislamiento o zona media/core, 2-4 series de 8-15 repeticiones", prompt);
        Assert.Contains(filtered, ex => ex.MuscleGroup == "Espalda");
    }

    [Fact]
    public void Given_PreseasonGeneralConditioning_When_FilteringExercises_Then_DistributesBalancedFullBody()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        const string goal = "pretemporada y acondicionamiento físico general";

        // Act
        var filtered = RoutinePromptBuilder.FilterExercisesForGoal(catalog, goal, maxExercises: 6);

        // Assert: Un objetivo general debe incluir una mezcla representativa sin monopolizar un solo grupo
        var distinctGroups = filtered.Select(e => e.MuscleGroup).Distinct().ToList();
        Assert.True(distinctGroups.Count >= 3, "Un acondicionamiento general debe distribuir entre al menos 3 grupos musculares.");
        Assert.Contains(filtered, e => e.MuscleGroup == "Piernas");
        Assert.Contains(filtered, e => e.MuscleGroup == "Pecho" || e.MuscleGroup == "Espalda");
    }

    [Fact]
    public void Given_PushStrengthGoal_When_BuildingBlockPrompt_Then_MandatesRestPeriodsAndCoachingCues()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        var request = new CreatePlanDraftRequestDto(
            Goal: "fuerza máxima de empuje y press banca",
            AthleteId: Guid.NewGuid(),
            Weeks: 2,
            DaysPerWeek: 3
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, catalog);

        // Assert
        Assert.Contains("En coachNotes indicá descansos recomendados (ej. 2-3 min en básicos, 60-90 s en accesorios)", prompt);
        Assert.Contains("foco técnico en fase excéntrica", prompt);
        Assert.Contains("75-85% RM", prompt);
    }

    [Fact]
    public void Given_LegHypertrophyGoal_When_FilteringExercises_Then_PrioritizesLegsOverOtherGroups()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        const string goal = "hipertrofia de piernas y cuádriceps";

        // Act
        var filtered = RoutinePromptBuilder.FilterExercisesForGoal(catalog, goal, maxExercises: 4);

        // Assert
        Assert.Equal(4, filtered.Count);
        Assert.All(filtered, e => Assert.Equal("Piernas", e.MuscleGroup));
    }

    [Fact]
    public void Given_CoreStabilityGoal_When_BuildingBlockPrompt_Then_PrioritizesCoreAndWarmUpWithoutRm()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        var request = new CreatePlanDraftRequestDto(
            Goal: "estabilidad de zona media y core",
            AthleteId: Guid.NewGuid(),
            Weeks: 2,
            DaysPerWeek: 2
        );

        // Act
        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, catalog);

        // Assert
        Assert.Contains("Core", prompt);
        Assert.Contains("warmUp: 1-2 ejercicios de entrada en calor, movilidad articular o activación neuromuscular. Sin porcentaje de RM", prompt);
    }

    [Fact]
    public void Given_CompetitiveCalendar_When_BuildingBlockPrompt_Then_EnforcesPreMatchTaperingDirectives()
    {
        // Arrange
        var catalog = CreateComprehensiveCatalog();
        var request = new CreatePlanDraftRequestDto(
            Goal: "fuerza competitiva",
            AthleteId: Guid.NewGuid(),
            Weeks: 2,
            DaysPerWeek: 3
        );
        var matchDates = new List<DateOnly> { new(2026, 11, 22) };

        // Act
        var prompt = RoutinePromptBuilder.BuildBlockPlanDraftPrompt(request, catalog, matchDates);

        // Assert
        Assert.Contains("Hay partidos oficiales en el calendario. No armes fuerza el día del partido y respetá D-1 <= 60% y D-2 <= 75%.", prompt);
    }

    [Fact]
    public void Given_DefaultAiConfigurations_When_InspectingParameters_Then_MatchesDocumentedTable()
    {
        // Arrange
        var geminiOptions = new GeminiOptions();
        var agentOptions = new AgentOptions();

        // Assert
        Assert.Equal(0.2, geminiOptions.Temperature);
        Assert.Equal(1200, geminiOptions.MaxOutputTokens);
        Assert.Equal(6, agentOptions.MaxToolCalls);
        Assert.Equal(40, agentOptions.TimeoutSeconds);
    }

    [Fact]
    public void Given_ProviderTimeouts_When_EvaluatingBudgets_Then_TotalIsStrictlyBelowGatewayThreshold()
    {
        // Arrange
        const int gatewayHardLimitSeconds = 100;
        const int frontendLimitSeconds = 120;

        var openAiTimeout = new AgentOptions().TimeoutSeconds;
        var geminiTimeout = new AgentOptions().TimeoutSeconds;

        var totalChainTimeout = openAiTimeout + geminiTimeout;

        // Assert: La suma de timeouts debe ser estrictamente menor a los 100 s del gateway
        Assert.True(totalChainTimeout < gatewayHardLimitSeconds, $"El timeout total ({totalChainTimeout}s) excede el gateway ({gatewayHardLimitSeconds}s).");
        Assert.True(totalChainTimeout < frontendLimitSeconds, $"El timeout total ({totalChainTimeout}s) excede el front ({frontendLimitSeconds}s).");
    }
}
