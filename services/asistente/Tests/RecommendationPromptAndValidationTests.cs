namespace Asistente.Tests;

using Application.Common.Recommendations;
using Application.DTOs.Recommendations;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias y de resiliencia para las instrucciones al modelo y la validación
/// de recomendaciones al PF (perfil frente al grupo) y de hábitos/alimentación (#083 · KAN-378).
/// </summary>
public sealed class RecommendationPromptAndValidationTests
{
    private static readonly IReadOnlyList<string> SampleApprovedFoods =
    [
        "pollo",
        "huevo",
        "arroz",
        "banana",
        "avena"
    ];

    #region 1. Construcción de Prompts (Entrenamiento y Alimentación)

    [Fact]
    public void BuildTrainingRecommendationPrompt_IncludesRuleCalculatedMetricsAndReason_WithoutPersonalData()
    {
        var request = new TrainingRecommendationRequestDto(
            Reason: "salto por debajo del promedio",
            Metrics:
            [
                new ProfileMetricComparisonDto(
                    Dimension: "Salto CMJ",
                    AthleteValue: 36.5,
                    GroupAverage: 42.0,
                    Unit: "cm",
                    PercentageDifference: -13.1,
                    Trend: "Por debajo del promedio (-13.1%)")
            ],
            CategoryName: "Primera División",
            SuggestedFocus: "Incorporar 2 bloques semanales de pliometría de bajo impacto y fuerza explosiva");

        var prompt = RecommendationPromptBuilder.BuildTrainingRecommendationPrompt(request);

        Assert.Contains("salto por debajo del promedio", prompt);
        Assert.Contains("Primera División", prompt);
        Assert.Contains("Salto CMJ: marca del atleta 36.5 cm vs promedio del grupo 42 cm (-13.1%)", prompt);
        Assert.Contains("LA IA SÓLO REDACTA", prompt);
        Assert.Contains("PRIVACIDAD", prompt);
        Assert.Contains("no menciones suplementos", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNutritionRecommendationPrompt_IncludesCalculatedNumbersAndApprovedFoodsList()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular y mejorar recuperación",
            ApprovedFoods: SampleApprovedFoods,
            Moment: "Día de entrenamiento pesado",
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            HydrationLiters: 2.8,
            SleepHours: 8,
            IncludeQuantities: true);

        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);

        Assert.Contains("Ganar masa muscular y mejorar recuperación", prompt);
        Assert.Contains("Día de entrenamiento pesado", prompt);
        Assert.Contains("entre 100 g y 140 g por día", prompt);
        Assert.Contains("2.8 litros de agua", prompt);
        Assert.Contains("8 horas de sueño", prompt);
        Assert.Contains("pollo, huevo, arroz, banana, avena", prompt);
        Assert.Contains("PROHIBIDO SUPLEMENTOS", prompt);
        Assert.Contains("PROHIBIDO BAJAR DE PESO", prompt);
        Assert.Contains("SÓLO ALIMENTOS DE LA LISTA", prompt);
    }

    [Fact]
    public void BuildNutritionRecommendationPrompt_WhenIncludeQuantitiesIsFalse_InstructsQualitativeModeWithoutGrams()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Rendimiento formativo",
            ApprovedFoods: SampleApprovedFoods,
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            IncludeQuantities: false);

        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);

        Assert.DoesNotContain("entre 100 g y 140 g", prompt);
        Assert.Contains("MODALIDAD SIN CANTIDADES", prompt);
        Assert.Contains("SIN CANTIDADES EN GRAMOS", prompt);
    }

    #endregion

    #region 2. Validación de Alimentación: Criterio de Aceptación Principal (Sin Suplementos y Sólo Alimentos de la Lista)

    [Fact]
    public void ValidateNutritionOutput_GivenValidResponseWithOnlyApprovedFoodsAndNoSupplements_ThenSucceeds()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: SampleApprovedFoods,
            Moment: "Día de entrenamiento pesado",
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            HydrationLiters: 2.5,
            SleepHours: 8);

        const string validAiJson = """
            {
              "suggestedPlateComposition": "Para este día pesado, armá tus comidas principales combinando pollo o huevo con arroz, y sumá avena con banana en la merienda para cubrir entre 100 g y 140 g de proteína diaria.",
              "hydrationAndSleepGuidelines": "Tomá al menos 2.5 litros de agua durante el día y asegurá 8 horas de sueño continuo.",
              "mentionedFoods": ["pollo", "huevo", "arroz", "avena", "banana"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(validAiJson, request);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value);
        Assert.Equal("Valido", result.ValidationCode);
        Assert.Equal(RecommendationPromptBuilder.OfficialNutritionDisclaimer, result.Value!.LegalDisclaimer);
        Assert.All(result.Value.MentionedFoods, food =>
            Assert.Contains(food, SampleApprovedFoods, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Sumá un batido de whey protein después de entrenar.")]
    [InlineData("Podés agregar 5 g de creatina junto con el arroz y el pollo.")]
    [InlineData("Complementá el desayuno de avena y banana con proteína en polvo.")]
    [InlineData("Tomá aminoácidos BCAA durante la sesión y comé pollo con arroz.")]
    [InlineData("Usá un suplemento pre-entreno antes de la práctica y banana.")]
    [InlineData("Agregá un quemador termogénico por la mañana junto al huevo.")]
    public void ValidateNutritionOutput_GivenAiMentionsSupplements_ThenRejectsWithSupplementCode(string plateWithSupplement)
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Recuperación muscular",
            ApprovedFoods: SampleApprovedFoods);

        var rawJson = $$"""
            {
              "suggestedPlateComposition": "{{plateWithSupplement}}",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJson, request);

        Assert.False(result.IsValid);
        Assert.Equal("SuplementoDetectado", result.ValidationCode);
        Assert.Contains("suplementos prohibidos", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Reducí las porciones de arroz y pollo para bajar de peso rápido.")]
    [InlineData("Mantené un déficit calórico de 500 kcal comiendo solo huevo y banana.")]
    [InlineData("Seguí una dieta hipocalórica estricta para adelgazar antes del torneo.")]
    [InlineData("Recortá calorías en la cena con pollo para quemar grasa.")]
    public void ValidateNutritionOutput_GivenAiSuggestsWeightLossOrDeficit_ThenRejectsWithWeightLossCode(string plateWithWeightLoss)
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Mejorar rendimiento",
            ApprovedFoods: SampleApprovedFoods);

        var rawJson = $$"""
            {
              "suggestedPlateComposition": "{{plateWithWeightLoss}}",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJson, request);

        Assert.False(result.IsValid);
        Assert.Equal("BajarDePesoDetectado", result.ValidationCode);
        Assert.Contains("bajar de peso", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateNutritionOutput_GivenIncludeQuantitiesIsFalseAndAiIncludesGrams_ThenRejectsWithQuantitiesCode()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Hábitos saludables",
            ApprovedFoods: SampleApprovedFoods,
            IncludeQuantities: false);

        const string rawJsonWithGrams = """
            {
              "suggestedPlateComposition": "Comé 150 g de pollo con 200 g de arroz y una banana.",
              "hydrationAndSleepGuidelines": "Hidratate con agua durante todo el día y descansá bien.",
              "mentionedFoods": ["pollo", "arroz", "banana"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJsonWithGrams, request);

        Assert.False(result.IsValid);
        Assert.Equal("CantidadesNoPermitidas", result.ValidationCode);
    }

    [Fact]
    public void ValidateNutritionOutput_GivenMentionedFoodsContainsItemOutsideApprovedList_ThenRejectsWithUnapprovedFoodCode()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: SampleApprovedFoods);

        const string rawJsonWithUnapprovedDeclaredFood = """
            {
              "suggestedPlateComposition": "Combiná pollo con arroz y huevo en tus comidas principales.",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz", "salmón"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJsonWithUnapprovedDeclaredFood, request);

        Assert.False(result.IsValid);
        Assert.Equal("AlimentoNoAprobado", result.ValidationCode);
        Assert.Contains("salmón", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateNutritionOutput_GivenFreeTextMentionsFoodOutsideApprovedList_ThenRejectsWithUnapprovedFoodCode()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Recuperación post-partido",
            ApprovedFoods: SampleApprovedFoods);

        // Aunque mentionedFoods solo declara pollo y arroz, en el texto cuela "salmón" y "almendras" que no están en SampleApprovedFoods
        const string rawJsonSneakingUnapprovedFood = """
            {
              "suggestedPlateComposition": "Armá el plato con pollo, arroz, salmón rosado y unas almendras.",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJsonSneakingUnapprovedFood, request);

        Assert.False(result.IsValid);
        Assert.Equal("AlimentoNoAprobado", result.ValidationCode);
    }

    [Theory]
    [InlineData("Armá tu plato con pollo, arroz y una milanesa al horno.")]
    [InlineData("Sumá unas fetas de salame junto al huevo y la banana.")]
    [InlineData("Combiná pollo con arroz y burrata artesanal para la cena.")]
    [InlineData("Comé focaccia junto con el pollo y el arroz.")]
    public void ValidateNutritionOutput_GivenUnlistedOrExoticUnapprovedFoodInPlateText_ThenRejectsWithUnapprovedFoodCode(string plateWithUnapprovedFood)
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: SampleApprovedFoods);

        var rawJson = $$"""
            {
              "suggestedPlateComposition": "{{plateWithUnapprovedFood}}",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJson, request);

        Assert.False(result.IsValid);
        Assert.Equal("AlimentoNoAprobado", result.ValidationCode);
    }

    [Theory]
    [InlineData("Te conviene comer menos en la cena, usando pollo y arroz.")]
    [InlineData("Comé menos cantidad de arroz y pollo durante la semana.")]
    [InlineData("Intentá reducir porciones en el almuerzo con pollo y huevo.")]
    [InlineData("Achicá tus porciones de arroz y evitá cenar después de entrenar.")]
    [InlineData("Podés saltearse comidas livianas y priorizar solo pollo con banana.")]
    public void ValidateNutritionOutput_GivenIndirectRestrictiveEatingPhrases_ThenRejectsWithWeightLossCode(string restrictivePlate)
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Mejorar rendimiento",
            ApprovedFoods: SampleApprovedFoods);

        var rawJson = $$"""
            {
              "suggestedPlateComposition": "{{restrictivePlate}}",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(rawJson, request);

        Assert.False(result.IsValid);
        Assert.Equal("BajarDePesoDetectado", result.ValidationCode);
    }

    #endregion

    #region 3. Validación de Recomendación de Entrenamiento (Perfil frente al grupo)

    [Fact]
    public void ValidateTrainingOutput_GivenValidJson_PreservesRuleCalculatedReasonAndSucceeds()
    {
        var request = new TrainingRecommendationRequestDto(
            Reason: "salto por debajo del promedio",
            Metrics:
            [
                new ProfileMetricComparisonDto("Salto CMJ", 35, 41, "cm", -14.6)
            ]);

        const string rawJson = """
            {
              "title": "Refuerzo de potencia en tren inferior",
              "reason": "salto por debajo del promedio",
              "recommendationText": "El atleta registra 35 cm en Salto CMJ frente a los 41 cm del promedio de su grupo (-14.6%). Sumá 2 estímulos semanales de pliometría controlada."
            }
            """;

        var result = RecommendationOutputValidator.ValidateTrainingOutput(rawJson, request);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value);
        Assert.Equal("salto por debajo del promedio", result.Value!.Reason);
        Assert.Contains("35 cm", result.Value.RecommendationText);
    }

    [Fact]
    public void ValidateTrainingOutput_GivenAiMentionsSupplements_ThenRejects()
    {
        var request = new TrainingRecommendationRequestDto(
            Reason: "salto por debajo del promedio");

        const string rawJson = """
            {
              "title": "Refuerzo de potencia",
              "reason": "salto por debajo del promedio",
              "recommendationText": "Sumá pliometría y suplementá con creatina todos los días."
            }
            """;

        var result = RecommendationOutputValidator.ValidateTrainingOutput(rawJson, request);

        Assert.False(result.IsValid);
        Assert.Equal("SuplementoDetectado", result.ValidationCode);
    }

    #endregion

    #region 4. Cadena Resiliente (ResilientAiService + Auditoría + Respaldo por Reglas)

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenPrimaryProviderMentionsSupplements_AuditsFailureAndFallsBackToRules()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: SampleApprovedFoods,
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            HydrationLiters: 2.5,
            SleepHours: 8,
            AthleteId: Guid.NewGuid());

        var badProvider = new Mock<IAiClient>();
        badProvider.Setup(p => p.ProviderName).Returns("Ollama");
        badProvider
            .Setup(p => p.TryGenerateNutritionRecommendationAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionRecommendationResponseDto(
                SuggestedPlateComposition: "Comé pollo con arroz y tomá un batido de whey protein.",
                HydrationAndSleepGuidelines: "Tomá 2.5 litros de agua y dormí 8 horas.",
                MentionedFoods: ["pollo", "arroz"],
                LegalDisclaimer: RecommendationPromptBuilder.OfficialNutritionDisclaimer,
                ProviderUsed: "Ollama"));

        var auditMock = new Mock<IAiAuditService>();
        var ruleClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock
            .Setup(c => c.GetHabitsPermissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteHabitsPermissionDto(IsMinor: false, AllowsHabitsRecommendations: false));

        var service = new ResilientAiService(
            providers: [badProvider.Object, ruleClient],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: auditMock.Object,
            catalogService: null,
            quotaService: null,
            structureAthleteClient: structureMock.Object,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: HabitsRecommendationsClientFactory.Enabled());

        var response = await service.GenerateNutritionRecommendationAsync(request);

        Assert.NotNull(response);
        Assert.Equal("Reglas", response.ProviderUsed);
        Assert.False(RecommendationOutputValidator.TryFindForbiddenSupplement(response.SuggestedPlateComposition, out _));
        Assert.All(response.MentionedFoods, food =>
            Assert.Contains(food, SampleApprovedFoods, StringComparer.OrdinalIgnoreCase));

        auditMock.Verify(
            a => a.LogAsync(
                It.Is<AiLog>(log =>
                    log.Provider == "Ollama" &&
                    !log.IsValid &&
                    log.ValidationResult == "SuplementoDetectado"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenPrimaryProviderUsesUnapprovedFood_FallsBackToSecondProvider()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Recuperación deportiva",
            ApprovedFoods: SampleApprovedFoods,
            MinProteinGrams: 100,
            MaxProteinGrams: 140);

        var badProvider = new Mock<IAiClient>();
        badProvider.Setup(p => p.ProviderName).Returns("Ollama");
        badProvider
            .Setup(p => p.TryGenerateNutritionRecommendationAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionRecommendationResponseDto(
                SuggestedPlateComposition: "Armá tu plato con salmón rosado y palta.",
                HydrationAndSleepGuidelines: "Tomá 2.5 litros de agua y descansá 8 horas.",
                MentionedFoods: ["salmón", "palta"],
                LegalDisclaimer: RecommendationPromptBuilder.OfficialNutritionDisclaimer,
                ProviderUsed: "Ollama"));

        var goodProvider = new Mock<IAiClient>();
        goodProvider.Setup(p => p.ProviderName).Returns("Gemini");
        goodProvider
            .Setup(p => p.TryGenerateNutritionRecommendationAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NutritionRecommendationResponseDto(
                SuggestedPlateComposition: "Armá tu plato con pollo, huevo y arroz, y sumá una banana de colación.",
                HydrationAndSleepGuidelines: "Tomá 2.5 litros de agua y descansá 8 horas.",
                MentionedFoods: ["pollo", "huevo", "arroz", "banana"],
                LegalDisclaimer: RecommendationPromptBuilder.OfficialNutritionDisclaimer,
                ProviderUsed: "Gemini"));

        var service = new ResilientAiService(
            providers: [badProvider.Object, goodProvider.Object],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: null,
            quotaService: null,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: HabitsRecommendationsClientFactory.Enabled());

        var response = await service.GenerateNutritionRecommendationAsync(request);

        Assert.Equal("Gemini", response.ProviderUsed);
        Assert.DoesNotContain("salmón", response.SuggestedPlateComposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pollo", response.SuggestedPlateComposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenGoalRequestsWeightLoss_ThrowsDomainException()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Dieta hipocalórica para bajar de peso rápido",
            ApprovedFoods: SampleApprovedFoods);

        var service = new ResilientAiService(
            providers: [],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: null,
            quotaService: null,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: HabitsRecommendationsClientFactory.Enabled());

        await Assert.ThrowsAsync<DomainException>(() =>
            service.GenerateNutritionRecommendationAsync(request));
    }

    [Fact]
    public async Task GenerateTrainingRecommendationAsync_WhenAiProvidersUnavailable_ReturnsRuleBasedRecommendationWithReason()
    {
        var request = new TrainingRecommendationRequestDto(
            Reason: "salto por debajo del promedio",
            Metrics:
            [
                new ProfileMetricComparisonDto("Salto CMJ", 34, 41, "cm", -17.1)
            ],
            CategoryName: "Reserva");

        var auditMock = new Mock<IAiAuditService>();
        var ruleClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var service = new ResilientAiService(
            providers: [ruleClient],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: auditMock.Object,
            catalogService: null,
            quotaService: null,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: HabitsRecommendationsClientFactory.Enabled());

        var response = await service.GenerateTrainingRecommendationAsync(request);

        Assert.Equal("Reglas", response.ProviderUsed);
        Assert.Equal("salto por debajo del promedio", response.Reason);
        Assert.Contains("Salto CMJ", response.RecommendationText);
    }

    #endregion

    #region 5. KAN-495: Listas blancas por grupo y aviso legal fijo

    [Fact]
    public void OfficialNutritionDisclaimer_MatchesTicketTextExactly()
    {
        Assert.Equal(
            "Esto es una recomendación general; para un plan a tu medida, consultá a un nutricionista.",
            RecommendationPromptBuilder.OfficialNutritionDisclaimer);
    }

    [Fact]
    public void DefaultApprovedFoodGroups_OrganizeOriginalFoodsIntoCompleteGroupsWithoutLoss()
    {
        var labels = RecommendationPromptBuilder.DefaultApprovedFoodGroups
            .Select(group => group.Label)
            .ToList();

        Assert.Equal(
            new[]
            {
                "Proteínas de Alta Calidad",
                "Hidratos de Carbono Complejos",
                "Frutas y Verduras / Hortalizas",
                "Grasas Saludables y Frutos Secos",
                RecommendationPromptBuilder.PreWorkoutSnackGroupLabel
            },
            labels);

        var union = RecommendationPromptBuilder.DefaultApprovedFoodGroups
            .SelectMany(group => group.Foods)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Sin pérdida: los 25 alimentos de la lista base original siguen todos presentes.
        var originalFoods = new[]
        {
            "pollo", "huevo", "carne vacuna magra", "atún", "merluza",
            "yogur natural", "queso magro", "leche", "lentejas", "garbanzos",
            "arroz", "fideos", "papa", "batata", "avena", "pan integral",
            "banana", "manzana", "naranja", "palta", "frutos secos",
            "aceite de oliva", "tomate", "zanahoria", "espinaca"
        };

        // Taxonomía expandida completa: 38 alimentos únicos (los 25 originales + los nuevos,
        // con el grupo de meriendas aportando sólo la opción combinada de digestión rápida).
        var allApprovedFoods = new[]
        {
            "pollo", "carne vacuna magra", "atún", "merluza", "huevo", "yogur natural",
            "queso magro", "leche", "lentejas", "garbanzos", "ricota magra", "pechuga de pavo",
            "arroz", "fideos", "papa", "batata", "avena", "pan integral",
            "quínoa", "galletas de arroz", "choclo", "calabaza",
            "banana", "manzana", "naranja", "pera", "frutillas",
            "espinaca", "tomate", "zanahoria", "brócoli", "zucchini",
            "aceite de oliva", "palta", "frutos secos", "mantequilla de maní", "semillas de chía",
            "galletas de arroz con mantequilla de maní"
        };

        Assert.Equal(allApprovedFoods.Length, union.Count);
        Assert.All(originalFoods, food => Assert.Contains(food, union, StringComparer.OrdinalIgnoreCase));
        Assert.All(allApprovedFoods, food => Assert.Contains(food, union, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(RecommendationPromptBuilder.DefaultApprovedFoods, union);
    }

    [Fact]
    public void BuildNutritionRecommendationPrompt_WithoutCustomFoods_ListsGroupedWhitelistAndPreWorkoutGuidance()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            Moment: "Antes del entrenamiento");

        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);

        Assert.Contains("Alimentos permitidos por grupo", prompt);
        Assert.Contains("* Proteínas de Alta Calidad: pollo, carne vacuna magra, atún, merluza, huevo, yogur natural, queso magro, leche, lentejas, garbanzos, ricota magra, pechuga de pavo", prompt);
        Assert.Contains("* Hidratos de Carbono Complejos: arroz, fideos, papa, batata, avena, pan integral, quínoa, galletas de arroz, choclo, calabaza", prompt);
        Assert.Contains("* Frutas y Verduras / Hortalizas: banana, manzana, naranja, pera, frutillas, espinaca, tomate, zanahoria, brócoli, zucchini", prompt);
        Assert.Contains("* Grasas Saludables y Frutos Secos: aceite de oliva, palta, frutos secos, mantequilla de maní, semillas de chía", prompt);
        Assert.Contains(
            $"* {RecommendationPromptBuilder.PreWorkoutSnackGroupLabel}: avena, banana, pan integral, yogur natural, frutos secos, manzana, galletas de arroz con mantequilla de maní",
            prompt);
        Assert.Contains("CONTEXTO PRE-ENTRENO", prompt);
        Assert.Contains("SÓLO ALIMENTOS DE LA LISTA", prompt);
    }

    [Fact]
    public void BuildNutritionRecommendationPrompt_IncludesFixedLegalDisclaimerInTemplate()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: SampleApprovedFoods);

        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(request);

        Assert.Contains("\"legalDisclaimer\"", prompt);
        Assert.Contains(RecommendationPromptBuilder.OfficialNutritionDisclaimer, prompt);
        Assert.Contains("AVISO LEGAL", prompt);
    }

    [Fact]
    public void BuildDeterministicNutritionRecommendation_ReturnsOfficialDisclaimerInLegalDisclaimerField()
    {
        var request = new NutritionRecommendationRequestDto(AthleticGoal: "Ganar masa muscular");

        var response = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request);

        Assert.Equal(RecommendationPromptBuilder.OfficialNutritionDisclaimer, response.LegalDisclaimer);
    }

    #endregion

    #region 6. Permiso del tutor para alimentación en menores (#201 · KAN-496)

    private static NutritionRecommendationRequestDto NutritionRequestWithAthlete(Guid? athleteId) =>
        new(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: SampleApprovedFoods,
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            HydrationLiters: 2.5,
            SleepHours: 8,
            AthleteId: athleteId);

    private static Mock<IStructureAthleteClient> StructureClientReturning(StructureAthleteHabitsPermissionDto? permission)
    {
        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock
            .Setup(c => c.GetHabitsPermissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);
        return structureMock;
    }

    private static ResilientAiService BuildServiceWith(
        IReadOnlyList<IAiClient> providers,
        IStructureAthleteClient? structureClient,
        IIdentityHabitsRecommendationsClient? identityClient = null) =>
        new(
            providers: providers,
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: null,
            quotaService: null,
            structureAthleteClient: structureClient,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: identityClient ?? HabitsRecommendationsClientFactory.Enabled());

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenMinorWithoutTutorPermission_ThrowsDomainException_WithoutCallingProviders()
    {
        var athleteId = Guid.NewGuid();
        var request = NutritionRequestWithAthlete(athleteId);

        var structureMock = StructureClientReturning(
            new StructureAthleteHabitsPermissionDto(IsMinor: true, AllowsHabitsRecommendations: false));

        var providerMock = new Mock<IAiClient>();
        providerMock.Setup(p => p.ProviderName).Returns("Ollama");

        var service = BuildServiceWith([providerMock.Object], structureMock.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateNutritionRecommendationAsync(request));

        providerMock.Verify(
            p => p.TryGenerateNutritionRecommendationAsync(It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        structureMock.Verify(
            c => c.GetHabitsPermissionAsync(athleteId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenMinorWithTutorPermission_GeneratesRecommendation()
    {
        var athleteId = Guid.NewGuid();
        var request = NutritionRequestWithAthlete(athleteId);

        var structureMock = StructureClientReturning(
            new StructureAthleteHabitsPermissionDto(IsMinor: true, AllowsHabitsRecommendations: true));

        var auditMock = new Mock<IAiAuditService>();
        var ruleClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var service = BuildServiceWith([ruleClient], structureMock.Object);

        var response = await service.GenerateNutritionRecommendationAsync(request);

        Assert.NotNull(response);
        structureMock.Verify(
            c => c.GetHabitsPermissionAsync(athleteId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenAdultWithoutHabitsFlag_GeneratesRecommendation()
    {
        var athleteId = Guid.NewGuid();
        var request = NutritionRequestWithAthlete(athleteId);

        var structureMock = StructureClientReturning(
            new StructureAthleteHabitsPermissionDto(IsMinor: false, AllowsHabitsRecommendations: false));

        var auditMock = new Mock<IAiAuditService>();
        var ruleClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var service = BuildServiceWith([ruleClient], structureMock.Object);

        var response = await service.GenerateNutritionRecommendationAsync(request);

        Assert.NotNull(response);
        Assert.Equal("Reglas", response.ProviderUsed);
    }

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenAthleteIdIsNull_SkipsPermissionCheck_AndGenerates()
    {
        var request = NutritionRequestWithAthlete(athleteId: null);

        var structureMock = StructureClientReturning(null);

        var auditMock = new Mock<IAiAuditService>();
        var ruleClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var service = BuildServiceWith([ruleClient], structureMock.Object);

        var response = await service.GenerateNutritionRecommendationAsync(request);

        Assert.NotNull(response);
        structureMock.Verify(
            c => c.GetHabitsPermissionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateNutritionRecommendationAsync_WhenEstructuraDoesNotRespond_ThrowsDomainException_WithoutCallingProviders()
    {
        var athleteId = Guid.NewGuid();
        var request = NutritionRequestWithAthlete(athleteId);

        // Estructura caída / 404 / 5xx: el cliente devuelve null y el motor no genera nada (fail-closed).
        var structureMock = StructureClientReturning(null);

        var providerMock = new Mock<IAiClient>();
        providerMock.Setup(p => p.ProviderName).Returns("Ollama");

        var service = BuildServiceWith([providerMock.Object], structureMock.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateNutritionRecommendationAsync(request));

        providerMock.Verify(
            p => p.TryGenerateNutritionRecommendationAsync(It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
