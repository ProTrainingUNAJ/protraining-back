using System.Net;
using System.Text;
using API.Controllers;
using Application.Common.Recommendations;
using Application.DTOs.Alerts;
using Application.DTOs.Recommendations;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Recommendations.ApproveHabitsRecommendation;
using Application.UseCases.Recommendations.GenerateHabitsRecommendation;
using Application.UseCases.Recommendations.GetHabitsRecommendation;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Proteína según peso/objetivo, momentos y bloqueos de seguridad.
/// Cubre reglas determinísticas, ficha médica real, ausencia de fuente de peso,
/// integración del caso de uso, contrato del controller y cliente auditado de Estructura.
/// </summary>
public sealed class Kan494HabitsRecommendationTests
{
    private static readonly Guid AthleteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAthleteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondCategoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    #region Reglas de cálculo y bloqueos

    private static MedicalRecommendationContext VerifiedMedical() =>
        HabitsMedicalContextRules.Evaluate(VerifiedRecord(), ArgentinaTime.Today);

    private static StructureMedicalRecordContextDto VerifiedRecord() =>
        new(
            StructureMedicalRecordAvailability.Available,
            HasPhysicalFitnessCertificate: true,
            PhysicalFitnessExpiresAt: ArgentinaTime.Today.AddDays(60),
            Pathologies: "Sin alergias",
            MedicalHistory: "Sin patologías",
            Surgeries: null);

    private static HabitsEvaluationResult Evaluate(
        string? goal,
        double? weightKg,
        bool isMinor = false,
        MedicalRecommendationContext? medical = null,
        bool? isHeavyTrainingDay = null,
        bool? hasNearbyMatch = null,
        bool? hasInsufficientRest = null,
        IReadOnlyList<string>? contextNotes = null) =>
        HabitsRecommendationRules.Evaluate(new HabitsEvaluationInput(
            AthleticGoal: goal,
            WeightKg: weightKg,
            IsMinor: isMinor,
            IsHeavyTrainingDay: isHeavyTrainingDay,
            HasNearbyMatch: hasNearbyMatch,
            HasInsufficientRest: hasInsufficientRest,
            MedicalContext: medical ?? VerifiedMedical(),
            ContextNotes: contextNotes));

    [Fact]
    public void Evaluate_Given70KgAndGainMuscle_Computes100To140Grams()
    {
        var result = Evaluate("Ganar masa muscular", 70);

        Assert.Equal(HabitsRecommendationRules.ModeQuantitative, result.RecommendationMode);
        Assert.True(result.IncludeQuantities);
        Assert.Equal(100d, result.MinProteinGrams!.Value);
        Assert.Equal(140d, result.MaxProteinGrams!.Value);
        Assert.False(result.RequiresProfessionalReferral);
        Assert.Contains("100-140", result.CalculationNote, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, 71, 100)]
    [InlineData(80, 114, 160)]
    public void Evaluate_GivenOtherWeightsAndGainMuscle_ScalesTheSameValidatedRatio(double weight, double min, double max)
    {
        var result = Evaluate("Hipertrofia", weight);

        Assert.Equal(min, result.MinProteinGrams!.Value);
        Assert.Equal(max, result.MaxProteinGrams!.Value);
    }

    [Theory]
    [InlineData(null, "Ganar masa muscular")]
    [InlineData(70d, null)]
    [InlineData(0d, "Ganar masa muscular")]
    [InlineData(-5d, "Ganar masa muscular")]
    public void Evaluate_WhenWeightOrGoalIsMissingOrInvalid_DoesNotInventQuantities(double? weight, string? goal)
    {
        var result = Evaluate(goal, weight);

        Assert.Null(result.MinProteinGrams);
        Assert.Null(result.MaxProteinGrams);
        Assert.False(result.IncludeQuantities);
        Assert.Equal(HabitsRecommendationRules.ModeQualitative, result.RecommendationMode);
        Assert.NotEmpty(result.MissingData);
    }

    [Fact]
    public void Evaluate_WhenMinor_HasNoQuantitiesEvenWithWeightGoalAndVerifiedRecord()
    {
        var result = Evaluate("Ganar masa muscular", 70, isMinor: true);

        Assert.Null(result.MinProteinGrams);
        Assert.Null(result.MaxProteinGrams);
        Assert.False(result.IncludeQuantities);
        Assert.Contains(result.MissingData!, m => m.Contains("menor de edad", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Bajar de peso")]
    [InlineData("Quiero perder kilos")]
    [InlineData("Dieta hipocalórica")]
    public void Evaluate_WhenGoalIsWeightLoss_RefusesAmountsAndStrategies(string goal)
    {
        var result = Evaluate(goal, 70);

        Assert.Equal(HabitsRecommendationRules.ModeReferral, result.RecommendationMode);
        Assert.True(result.RequiresProfessionalReferral);
        Assert.Null(result.MinProteinGrams);
        Assert.Null(result.MaxProteinGrams);
        Assert.False(result.IncludeQuantities);
        Assert.NotEmpty(result.ReferralReasons);
        Assert.DoesNotContain("bajar de peso", result.PlateGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MedicalRecommendationStatus.RegisteredRisk)]
    [InlineData(MedicalRecommendationStatus.Incomplete)]
    [InlineData(MedicalRecommendationStatus.Unavailable)]
    public void Evaluate_WhenMedicalRiskIsNotCleared_RefersWithoutQuantities(MedicalRecommendationStatus status)
    {
        var medical = new MedicalRecommendationContext(
            status,
            ["Motivo de seguridad médica."],
            ["Dato médico pendiente."]);

        var result = Evaluate("Ganar masa muscular", 70, medical: medical);

        Assert.Equal(HabitsRecommendationRules.ModeReferral, result.RecommendationMode);
        Assert.True(result.RequiresProfessionalReferral);
        Assert.Null(result.MinProteinGrams);
        Assert.Null(result.MaxProteinGrams);
    }

    [Fact]
    public void Evaluate_WhenNoMedicalContextWasQueried_RefersWithoutQuantities()
    {
        var evaluation = HabitsRecommendationRules.Evaluate(new HabitsEvaluationInput(
            AthleticGoal: "Ganar masa muscular",
            WeightKg: 70,
            IsMinor: false,
            IsHeavyTrainingDay: null,
            HasNearbyMatch: true,
            HasInsufficientRest: false));

        Assert.True(evaluation.RequiresProfessionalReferral);
        Assert.Null(evaluation.MinProteinGrams);
        Assert.Null(evaluation.MaxProteinGrams);
        Assert.Contains(evaluation.MissingData, m => m.Contains("Ficha médica no consultada", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_WithAllMoments_DetectsThemAndAddsConservativeGuidance()
    {
        var result = Evaluate(
            "Ganar masa muscular",
            70,
            isHeavyTrainingDay: true,
            hasNearbyMatch: true,
            hasInsufficientRest: true);

        Assert.Contains("Día pesado de entrenamiento", result.DetectedMoments);
        Assert.Contains("Partido cercano", result.DetectedMoments);
        Assert.Contains("Poco descanso", result.DetectedMoments);
        Assert.Contains("día pesado", result.PlateGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partido", result.PlateGuidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("descanso", result.HydrationAndSleepGuidelines, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenMomentDataIsUnknown_MarksItAsMissing()
    {
        var result = Evaluate("Ganar masa muscular", 70);

        Assert.DoesNotContain("Partido cercano", result.DetectedMoments);
        Assert.Contains(result.MissingData!, m => m.Contains("día pesado", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingData!, m => m.Contains("partido cercano", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.MissingData!, m => m.Contains("descanso", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InferMomentsFromData_UsesApprovedAssumptionsAndReturnsNullWithoutData()
    {
        var today = ArgentinaTime.Today;

        var nearby = HabitsRecommendationRules.InferMomentsFromData([today.AddDays(2)], today, null);
        Assert.True(nearby.hasNearbyMatch);

        var far = HabitsRecommendationRules.InferMomentsFromData([today.AddDays(3)], today, null);
        Assert.False(far.hasNearbyMatch);

        var unknown = HabitsRecommendationRules.InferMomentsFromData(null, today, []);
        Assert.Null(unknown.hasNearbyMatch);
        Assert.Null(unknown.hasInsufficientRest);

        var shortSleep = new AthleteCheckInHistoryItemDto(
            Guid.NewGuid(), AthleteId, today, 5.0, 3, 3, 3, 3, 3, null, false, null, false);
        var longSleep = shortSleep with { SleepHours = 8.0 };

        Assert.True(HabitsRecommendationRules.InferMomentsFromData(null, today, [shortSleep]).hasInsufficientRest);
        Assert.False(HabitsRecommendationRules.InferMomentsFromData(null, today, [longSleep]).hasInsufficientRest);
    }

    #endregion

    #region Ficha médica real

    [Fact]
    public void MedicalRules_GivenExplicitNegativesAndCurrentFitness_AllowsQuantitativeMode()
    {
        var context = HabitsMedicalContextRules.Evaluate(VerifiedRecord(), ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.VerifiedNoKnownRisk, context.Status);
        Assert.False(context.RequiresReferral);
        Assert.Empty(context.ReferralReasons);
        Assert.Contains(context.MissingData, m => m.Contains("texto libre", StringComparison.OrdinalIgnoreCase));
    }

    // Antes "na" se borraba como subcadena: "renal" quedaba "re l" y "anafilaxia", "a filaxia", y esas fichas
    // recibían gramos de proteína. Una negación tampoco puede anular un riesgo que está en otra parte del texto.
    [Theory]
    [InlineData("Insuficiencia renal crónica", "Sin alergias")]
    [InlineData("Enfermedad renal", "Sin alergias")]
    [InlineData("Anafilaxia a la penicilina", "Sin antecedentes")]
    [InlineData("Sin alergias. Insuficiencia renal", "Sin antecedentes")]
    [InlineData("Sin patologías", "Sin alergias, asma")]
    public void MedicalRules_GivenConditionNextToNegation_RequiresReferral(string pathologies, string history)
    {
        var record = VerifiedRecord() with { Pathologies = pathologies, MedicalHistory = history };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.RegisteredRisk, context.Status);
        Assert.True(context.RequiresReferral);
    }

    [Theory]
    [InlineData("N/A", "na")]
    [InlineData("-", "s/d")]
    public void MedicalRules_GivenPlaceholdersOnly_TreatsRecordAsEmpty(string pathologies, string history)
    {
        var record = VerifiedRecord() with { Pathologies = pathologies, MedicalHistory = history, Surgeries = null };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.Incomplete, context.Status);
        Assert.True(context.RequiresReferral);
    }

    [Fact]
    public void MedicalRules_GivenTextWithoutExplicitNegation_RequiresReferral()
    {
        var record = VerifiedRecord() with
        {
            Pathologies = "Juega al fútbol tres veces por semana",
            MedicalHistory = null,
            Surgeries = null
        };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.Incomplete, context.Status);
        Assert.True(context.RequiresReferral);
    }

    [Fact]
    public void MedicalRules_GivenRegisteredAllergy_RequiresReferral()
    {
        var record = VerifiedRecord() with { Pathologies = "Alergia a la penicilina" };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.RegisteredRisk, context.Status);
        Assert.True(context.RequiresReferral);
    }

    [Fact]
    public void MedicalRules_GivenStructuredAllergiesOrConditions_RequiresReferral()
    {
        var allergies = HabitsMedicalContextRules.Evaluate(
            VerifiedRecord() with { HasKnownAllergies = true, HasKnownConditions = false },
            ArgentinaTime.Today);
        var conditions = HabitsMedicalContextRules.Evaluate(
            VerifiedRecord() with { HasKnownAllergies = false, HasKnownConditions = true },
            ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.RegisteredRisk, allergies.Status);
        Assert.Equal(MedicalRecommendationStatus.RegisteredRisk, conditions.Status);
        Assert.Contains(allergies.MissingData, m => m.Contains("estructurados", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MedicalRules_GivenStructuredNegativesAndEmptyText_AllowsQuantitativeMode()
    {
        var record = VerifiedRecord() with
        {
            Pathologies = null,
            MedicalHistory = null,
            Surgeries = null,
            HasKnownAllergies = false,
            HasKnownConditions = false
        };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.VerifiedNoKnownRisk, context.Status);
        Assert.False(context.RequiresReferral);
        Assert.Contains(context.MissingData, m => m.Contains("estructurados", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MedicalRules_GivenStructuredFalseButClinicalRiskInText_StillRefers()
    {
        var record = VerifiedRecord() with
        {
            Pathologies = "Alergia a la penicilina",
            HasKnownAllergies = false,
            HasKnownConditions = false
        };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.RegisteredRisk, context.Status);
        Assert.True(context.RequiresReferral);
    }

    [Fact]
    public void MedicalRules_GivenPartialStructuredNegativesAndEmptyText_RemainsIncomplete()
    {
        var record = VerifiedRecord() with
        {
            Pathologies = null,
            MedicalHistory = null,
            Surgeries = null,
            HasKnownAllergies = false,
            HasKnownConditions = null
        };

        Assert.Equal(
            MedicalRecommendationStatus.Incomplete,
            HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today).Status);
    }

    [Fact]
    public void MedicalRules_GivenEmptyOrMissingRecord_NeverAssumesNoAllergies()
    {
        var empty = HabitsMedicalContextRules.Evaluate(
            VerifiedRecord() with { Pathologies = null, MedicalHistory = null, Surgeries = null },
            ArgentinaTime.Today);
        var noRecord = HabitsMedicalContextRules.Evaluate(
            new StructureMedicalRecordContextDto(
                StructureMedicalRecordAvailability.NoRecord, true, null, null, null, null),
            ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.Incomplete, empty.Status);
        Assert.Equal(MedicalRecommendationStatus.Incomplete, noRecord.Status);
        Assert.True(empty.RequiresReferral);
        Assert.True(noRecord.RequiresReferral);
        Assert.Contains(empty.MissingData, m => m.Contains("no se asume ausencia de alergias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MedicalRules_GivenUnavailableRecord_RequiresReferral()
    {
        var context = HabitsMedicalContextRules.Evaluate(null, ArgentinaTime.Today);

        Assert.Equal(MedicalRecommendationStatus.Unavailable, context.Status);
        Assert.True(context.RequiresReferral);
    }

    [Fact]
    public void MedicalRules_GivenExpiredFitness_KeepsSafetyNote()
    {
        var record = VerifiedRecord() with { PhysicalFitnessExpiresAt = ArgentinaTime.Today.AddDays(-1) };

        var context = HabitsMedicalContextRules.Evaluate(record, ArgentinaTime.Today);

        Assert.True(context.Status is MedicalRecommendationStatus.VerifiedNoKnownRisk or MedicalRecommendationStatus.RegisteredRisk);
        Assert.Contains(context.MissingData, m => m.Contains("apto físico", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Prompt y validación sin cantidades inventadas

    [Fact]
    public void DeterministicFallback_InNoQuantityMode_HasNoInventedNumbersOrSupplements()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            IncludeQuantities: false,
            RequiresProfessionalReferral: true,
            RecommendationMode: HabitsRecommendationRules.ModeReferral,
            ReferralReasons: ["Motivo de seguridad."],
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            HydrationLiters: 2.5,
            SleepHours: 8.0);

        var drafted = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request);

        Assert.Null(drafted.MinProteinGrams);
        Assert.Null(drafted.MaxProteinGrams);
        Assert.DoesNotContain("2.5", drafted.HydrationAndSleepGuidelines, StringComparison.Ordinal);
        Assert.DoesNotContain("8 horas", drafted.HydrationAndSleepGuidelines, StringComparison.Ordinal);
        Assert.False(RecommendationOutputValidator.TryFindForbiddenSupplement(
            $"{drafted.SuggestedPlateComposition} {drafted.HydrationAndSleepGuidelines}", out _));
        Assert.True(RecommendationOutputValidator.ValidateNutritionDto(drafted, request).IsValid);
    }

    [Fact]
    public void DeterministicFallback_WithoutRuleProvidedHydrationOrSleep_DoesNotUseDefaultNumbers()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            IncludeQuantities: true,
            MinProteinGrams: 100,
            MaxProteinGrams: 140);

        var drafted = RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request);

        Assert.Equal(100d, drafted.MinProteinGrams!.Value);
        Assert.Equal(140d, drafted.MaxProteinGrams!.Value);
        Assert.DoesNotContain("litros", drafted.HydrationAndSleepGuidelines, StringComparison.Ordinal);
        Assert.Contains("descanso", drafted.HydrationAndSleepGuidelines, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_WhenNoQuantitiesAllowed_RejectsNumericHydrationAndSleep()
    {
        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Hábitos saludables",
            IncludeQuantities: false);

        const string json = """
            {
              "suggestedPlateComposition": "Combiná pollo con arroz y banana en tus comidas.",
              "hydrationAndSleepGuidelines": "Tomá 2.5 litros de agua y dormí 8 horas.",
              "mentionedFoods": ["pollo", "arroz", "banana"]
            }
            """;

        var result = RecommendationOutputValidator.ValidateNutritionOutput(json, request);

        Assert.False(result.IsValid);
        Assert.Equal("CantidadesNoPermitidas", result.ValidationCode);
    }

    [Fact]
    public void NutritionPrompt_ForNoQuantityMode_ExplicitlyForbidsInventedAmountsAndSupplements()
    {
        var prompt = RecommendationPromptBuilder.BuildNutritionRecommendationPrompt(
            new NutritionRecommendationRequestDto(
                AthleticGoal: "Ganar masa muscular",
                IncludeQuantities: false,
                IsMinor: true));

        Assert.Contains("MODALIDAD SIN CANTIDADES", prompt, StringComparison.Ordinal);
        Assert.Contains("PROHIBIDO SUPLEMENTOS", prompt, StringComparison.Ordinal);
        Assert.Contains("litros u horas", prompt, StringComparison.Ordinal);
    }

    #endregion

    #region Caso de uso (handler)

    private static AthleteCheckInHistoryItemDto CheckIn(DateOnly date, double sleepHours) =>
        new(Guid.NewGuid(), AthleteId, date, sleepHours, 3, 3, 3, 3, 3, null, false, null, false);

    private sealed class HandlerFixture
    {
        public Mock<IStructureAthleteClient> Structure { get; } = new();
        public IIdentityHabitsRecommendationsClient Identity { get; }
        public Mock<ISeguimientoClient> Seguimiento { get; } = new();
        public Mock<IStructureCalendarClient> Calendar { get; } = new();
        public Mock<IAiService> Ai { get; } = new();
        public Mock<IAthleteRecommendationRepository> Repository { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IHabitsAthleteContextProvider> Provider { get; } = new();
        public Mock<IHabitsTrainingDayContextProvider> TrainingDayProvider { get; } = new();
        public AthleteRecommendation? Saved { get; private set; }
        public NutritionRecommendationRequestDto? AiRequest { get; private set; }

        public HandlerFixture(bool pfEnabled = true, bool isMinor = false)
        {
            Identity = HabitsRecommendationsClientFactory.Create(pfEnabled, pfEnabled);

            Structure.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Structure.SetupHabitsPermission(AthleteId, isMinor, allows: true);
            Structure.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera")]);
            Structure.Setup(s => s.GetMedicalRecordContextAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(VerifiedRecord());

            Seguimiento.Setup(s => s.GetAthleteCheckInsHistoryAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<DateOnly>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Calendar.Setup(c => c.GetMatchDatesAsync(
                    It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Provider.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AthleteNutritionContextDto?)null);

            TrainingDayProvider.Setup(p => p.IsHeavyTrainingDayAsync(
                    It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((bool?)null);

            Repository.Setup(r => r.ListAsync(
                    It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<RecommendationStatus?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Repository.Setup(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()))
                .Callback<AthleteRecommendation, CancellationToken>((entity, _) => Saved = entity)
                .Returns(Task.CompletedTask);

            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Ai.Setup(a => a.GenerateNutritionRecommendationAsync(
                    It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
                .Callback<NutritionRecommendationRequestDto, CancellationToken>((request, _) => AiRequest = request)
                .ReturnsAsync((NutritionRecommendationRequestDto request, CancellationToken _) =>
                    RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request, "IA"));
        }

        public GetHabitsRecommendationForAthleteQueryHandler BuildHandler()
        {
            var generate = new GenerateHabitsRecommendationCommandHandler(
                Structure.Object,
                Seguimiento.Object,
                Calendar.Object,
                Repository.Object,
                Ai.Object,
                UnitOfWork.Object,
                Provider.Object,
                TrainingDayProvider.Object);

            return new GetHabitsRecommendationForAthleteQueryHandler(
                Structure.Object,
                Identity,
                Repository.Object,
                generate);
        }

        public void SetContext(string? goal, double? weight) =>
            Provider.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AthleteNutritionContextDto(goal, weight));
    }

    [Fact]
    public async Task Handler_GivenWeightAndGoalFromProvider_GeneratesQuantitativeSnapshot()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Equal(100d, response.MinProteinGrams!.Value);
        Assert.Equal(140d, response.MaxProteinGrams!.Value);
        Assert.Equal(120d, response.DailyProteinRequirementGrams!.Value);
        Assert.Equal(70d, response.WeightKg!.Value);
        Assert.Equal(HabitsRecommendationRules.ModeQuantitative, response.RecommendationMode);
        Assert.False(response.RequiresProfessionalReferral);
        Assert.NotNull(fixture.Saved);

        var (_, _, habits) = RecommendationMapper.DeserializeFullSnapshot(fixture.Saved!.MetricsSnapshotJson);
        Assert.NotNull(habits);
        Assert.Equal(70d, habits!.WeightKg);
        Assert.Equal(100d, habits.MinProteinGrams!.Value);
        Assert.Equal(140d, habits.MaxProteinGrams!.Value);
        Assert.Contains("100-140", habits.CalculationNote, StringComparison.Ordinal);

        fixture.Provider.Verify(p => p.GetAsync(AthleteId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Structure.Verify(s => s.GetMedicalRecordContextAsync(
            AthleteId,
            HabitsMedicalContextRules.MedicalRecordJustification,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_WhenAiReturnsInventedRanges_BackendKeepsRulesValues()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Ai.Setup(a => a.GenerateNutritionRecommendationAsync(
                It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NutritionRecommendationRequestDto request, CancellationToken _) =>
                RecommendationPromptBuilder.BuildDeterministicNutritionRecommendation(request, "IA")
                with { MinProteinGrams = 999, MaxProteinGrams = 1000, RecommendationMode = HabitsRecommendationRules.ModeReferral });

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Equal(100d, response.MinProteinGrams!.Value);
        Assert.Equal(140d, response.MaxProteinGrams!.Value);
        Assert.Equal(HabitsRecommendationRules.ModeQuantitative, response.RecommendationMode);
    }

    [Fact]
    public async Task Handler_WhenNoWeightOrGoalSource_DoesNotInventValuesOrAmounts()
    {
        var fixture = new HandlerFixture();

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Null(response.WeightKg);
        Assert.Null(response.MinProteinGrams);
        Assert.Null(response.MaxProteinGrams);
        Assert.Equal(HabitsRecommendationRules.ModeQualitative, response.RecommendationMode);
        Assert.Contains(response.MissingData!, m => m.Contains("Peso corporal no disponible", StringComparison.Ordinal));
        Assert.Contains(response.MissingData!, m => m.Contains("Objetivo deportivo no informado", StringComparison.Ordinal));
        Assert.DoesNotContain("100-140", response.SuggestedPlateComposition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_WhenMedicalRecordIsUnavailable_RefersWithoutQuantities()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Structure.Setup(s => s.GetMedicalRecordContextAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureMedicalRecordContextDto(
                StructureMedicalRecordAvailability.Unavailable, false, null, null, null, null));

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.True(response.RequiresProfessionalReferral);
        Assert.Equal(HabitsRecommendationRules.ModeReferral, response.RecommendationMode);
        Assert.Null(response.MinProteinGrams);
        Assert.Null(response.MaxProteinGrams);
        Assert.Contains(response.ReferralReasons!, r => r.Contains("ficha médica", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(fixture.AiRequest);
        Assert.False(fixture.AiRequest!.IncludeQuantities);
        Assert.True(fixture.AiRequest.RequiresProfessionalReferral);
    }

    [Fact]
    public async Task Handler_WhenWeightLossGoal_NeverGeneratesAmountsOrLossStrategies()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Bajar de peso", 70);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Equal(HabitsRecommendationRules.ModeReferral, response.RecommendationMode);
        Assert.True(response.RequiresProfessionalReferral);
        Assert.Null(response.MinProteinGrams);
        Assert.Null(response.MaxProteinGrams);
        Assert.DoesNotContain("150 g", response.SuggestedPlateComposition, StringComparison.Ordinal);
        Assert.DoesNotContain("déficit", response.SuggestedPlateComposition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handler_WhenMinor_OmitsQuantitiesEvenWithWeightAndGoal()
    {
        var fixture = new HandlerFixture(isMinor: true);
        fixture.SetContext("Ganar masa muscular", 70);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.True(response.IsMinor);
        Assert.Null(response.MinProteinGrams);
        Assert.Null(response.MaxProteinGrams);
        Assert.Contains(response.MissingData!, m => m.Contains("menor de edad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_WhenMultipleCategories_QueriesAllCalendarsForNearbyMatch()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Structure.Setup(s => s.GetAthleteCategoriesAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StructureCategoryDto(CategoryId, "Primera"),
                new StructureCategoryDto(SecondCategoryId, "Reserva")
            ]);
        fixture.Calendar.Setup(c => c.GetMatchDatesAsync(
                SecondCategoryId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([ArgentinaTime.Today.AddDays(1)]);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Contains("Partido cercano", response.DetectedMoments!);
        fixture.Calendar.Verify(c => c.GetMatchDatesAsync(
            CategoryId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Calendar.Verify(c => c.GetMatchDatesAsync(
            SecondCategoryId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_WhenCalendarsFail_DoesNotInventNearbyMatch()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Calendar.Setup(c => c.GetMatchDatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("Calendario no disponible"));

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.DoesNotContain("Partido cercano", response.DetectedMoments!);
        Assert.Contains(response.MissingData!, m => m.Contains("partido cercano", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_WhenCheckInsFail_DoesNotInventRestState()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Seguimiento.Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SeguimientoServiceUnavailableException("Seguimiento no disponible"));

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.DoesNotContain("Poco descanso", response.DetectedMoments!);
        Assert.Contains(response.MissingData!, m => m.Contains("descanso reciente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_WhenCheckInsHaveFutureAndOldDates_DiscardsFutureAndFlagsStaleData()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        var today = ArgentinaTime.Today;
        fixture.Seguimiento.Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([CheckIn(today.AddDays(1), 4), CheckIn(today.AddDays(-1), 5)]);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Contains("Poco descanso", response.DetectedMoments!);
        Assert.Contains(response.MissingData!, m => m.Contains("fecha futura", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.MissingData!, m => m.Contains("último check-in", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_WhenTrainingDayProviderReportsHeavyDay_DetectsMoment()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.TrainingDayProvider.Setup(p => p.IsHeavyTrainingDayAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Contains("Día pesado de entrenamiento", response.DetectedMoments!);
        Assert.DoesNotContain(response.MissingData!, m => m.Contains("día pesado", StringComparison.OrdinalIgnoreCase));
        fixture.TrainingDayProvider.Verify(p => p.IsHeavyTrainingDayAsync(
            AthleteId, ArgentinaTime.Today, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_WhenTrainingDayProviderFails_DoesNotInventHeavyDay()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.TrainingDayProvider.Setup(p => p.IsHeavyTrainingDayAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fuente de día pesado no disponible"));

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.DoesNotContain("Día pesado de entrenamiento", response.DetectedMoments!);
        Assert.Contains(response.MissingData!, m => m.Contains("día pesado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_WhenCalendarThrowsCancellation_Propagates()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Calendar.Setup(c => c.GetMatchDatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));
    }

    [Fact]
    public async Task Handler_WhenCheckInsThrowCancellation_Propagates()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Seguimiento.Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));
    }

    [Fact]
    public async Task Handler_WhenTrainingDayProviderThrowsCancellation_Propagates()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.TrainingDayProvider.Setup(p => p.IsHeavyTrainingDayAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));
    }

    [Fact]
    public async Task Handler_WhenAiThrowsDomainException_DoesNotGenerateOrPersist()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Ai.Setup(a => a.GenerateNutritionRecommendationAsync(
                It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Bloqueo de política."));

        await Assert.ThrowsAsync<DomainException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));

        fixture.Repository.Verify(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_WhenAiThrowsTechnicalError_FallsBackToRulesAndPersists()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Ai.Setup(a => a.GenerateNutritionRecommendationAsync(
                It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Servicio de IA no disponible"));

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Equal(100d, response.MinProteinGrams!.Value);
        Assert.Equal(140d, response.MaxProteinGrams!.Value);
        Assert.NotNull(fixture.Saved);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_WhenAiThrowsCancellation_Propagates()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);
        fixture.Ai.Setup(a => a.GenerateNutritionRecommendationAsync(
                It.IsAny<NutritionRecommendationRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));
    }

    [Fact]
    public async Task Handler_WithoutTrainingDaySource_ReportsItAsMissing()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.DoesNotContain("Día pesado de entrenamiento", response.DetectedMoments!);
        Assert.Contains(response.MissingData!, m => m.Contains("día pesado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handler_WhenHabitsAreDisabledByKan493_BlocksBeforeReadingContext()
    {
        var fixture = new HandlerFixture(pfEnabled: false);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));

        fixture.Provider.Verify(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Structure.Verify(s => s.GetMedicalRecordContextAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Repository.Verify(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_WhenAthleteIsNotOwn_ThrowsNotFound()
    {
        var fixture = new HandlerFixture();
        fixture.Structure.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId)));

        fixture.Provider.Verify(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_WhenPendingHabitExists_ReusesItWithoutRecomputing()
    {
        var fixture = new HandlerFixture();
        var snapshot = new HabitsRecommendationSnapshotDto(
            AthleticGoal: "Objetivo previo",
            WeightKg: 42,
            MinProteinGrams: null,
            MaxProteinGrams: null,
            DailyProteinRequirementGrams: null,
            SuggestedPlateComposition: "Texto previo",
            HydrationAndSleepGuidelines: "Hidratación previa",
            LegalDisclaimer: RecommendationPromptBuilder.OfficialNutritionDisclaimer,
            RecommendationMode: HabitsRecommendationRules.ModeQualitative,
            IsMinor: false,
            RequiresProfessionalReferral: false,
            ReferralReasons: [],
            DetectedMoments: [],
            MissingData: [],
            MentionedFoods: [],
            CalculationNote: null);
        var existing = RecommendationMapper.CreatePendingHabitsEntity(
            AthleteId, CategoryId, "Primera", "Título", "Motivo", "Texto", snapshot);
        fixture.Repository.Setup(r => r.ListAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<RecommendationStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([existing]);

        var response = await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(AthleteId));

        Assert.Equal("Objetivo previo", response.AthleticGoal);
        Assert.Equal(42d, response.WeightKg!.Value);
        Assert.False(response.ApprovedByPf);
        fixture.Provider.Verify(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.TrainingDayProvider.Verify(p => p.IsHeavyTrainingDayAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        // La Query de lectura no escribe: la generación/persistencia quedó en el Command.
        fixture.Repository.Verify(r => r.AddAsync(It.IsAny<AthleteRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_UsesRequestedAthleteIdForAllContextSources()
    {
        var fixture = new HandlerFixture();
        fixture.Structure.Setup(s => s.IsOwnAthleteAsync(SecondAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Structure.SetupHabitsPermission(SecondAthleteId);
        fixture.Structure.Setup(s => s.GetAthleteCategoriesAsync(SecondAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StructureCategoryDto(CategoryId, "Primera")]);

        await fixture.BuildHandler().HandleAsync(new GetHabitsRecommendationForAthleteQuery(SecondAthleteId));

        fixture.Structure.Verify(s => s.IsOwnAthleteAsync(SecondAthleteId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Structure.Verify(s => s.GetHabitsPermissionAsync(SecondAthleteId, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Structure.Verify(s => s.GetMedicalRecordContextAsync(
            SecondAthleteId, HabitsMedicalContextRules.MedicalRecordJustification, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Provider.Verify(p => p.GetAsync(SecondAthleteId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(fixture.Saved);
        Assert.Equal(SecondAthleteId, fixture.Saved!.AthleteId);
    }

    #endregion

    #region Controller y aprobación

    [Fact]
    public async Task Controller_GetAndApproveHabitsRecommendation_ReturnOkAndPublishForAthlete()
    {
        var fixture = new HandlerFixture();
        fixture.SetContext("Ganar masa muscular", 70);

        var approveRepository = new Mock<IAthleteRecommendationRepository>();
        var approveUnitOfWork = new Mock<IUnitOfWork>();
        var target = RecommendationMapper.CreatePendingHabitsEntity(
            AthleteId,
            CategoryId,
            "Primera",
            "Recomendación de hábitos y proteína",
            "Motivo",
            "Texto",
            new HabitsRecommendationSnapshotDto(
                "Ganar masa muscular", 70, 100, 140, 120, "Plato", "Hidratación",
                RecommendationPromptBuilder.OfficialNutritionDisclaimer,
                HabitsRecommendationRules.ModeQuantitative, false, false, [], [], [], [], null));
        approveRepository.Setup(r => r.GetByIdForUpdateAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        approveUnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var controller = new HabitsRecommendationsController(
            fixture.BuildHandler(),
            new ApproveHabitsRecommendationCommandHandler(approveRepository.Object, approveUnitOfWork.Object));

        var getResult = await controller.GetHabitsRecommendation(AthleteId);
        var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
        var dto = Assert.IsType<HabitsRecommendationResponseDto>(getOk.Value);
        Assert.Equal(100d, dto.MinProteinGrams!.Value);

        var approveResult = await controller.ApproveHabitsRecommendation(target.Id);
        var approveOk = Assert.IsType<OkObjectResult>(approveResult.Result);
        var message = Assert.IsType<ApproveHabitsRecommendationResponseDto>(approveOk.Value);
        Assert.Equal(ApproveHabitsRecommendationCommandHandler.ApprovedMessage, message.Message);
        Assert.Equal(RecommendationStatus.Approved, target.Status);
    }

    [Fact]
    public async Task ApproveHandler_WhenRecommendationIsNotHabits_ThrowsNotFound()
    {
        var repository = new Mock<IAthleteRecommendationRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var training = AthleteRecommendation.Create(AthleteId, "Entrenamiento", "Motivo", "Texto");
        repository.Setup(r => r.GetByIdForUpdateAsync(training.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(training);

        var handler = new ApproveHabitsRecommendationCommandHandler(repository.Object, unitOfWork.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new ApproveHabitsRecommendationCommand(training.Id)));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Cliente HTTP de ficha médica (endpoint auditado)

    private sealed class DelegatingTestHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static StructureAthleteClient BuildStructureClient(DelegatingTestHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://estructura:8080/") };
        return new StructureAthleteClient(httpClient, NullLogger<StructureAthleteClient>.Instance);
    }

    [Fact]
    public async Task MedicalClient_WhenEndpointReturnsRecord_ReturnsAvailableAndSendsJustification()
    {
        var recordId = Guid.NewGuid();
        var json = $$"""
            {
              "id": "{{recordId}}",
              "athleteId": "{{AthleteId}}",
              "medicalHistory": "Sin patologías",
              "surgeries": null,
              "pathologies": "Sin alergias",
              "hasPhysicalFitnessCertificate": true,
              "physicalFitnessExpiresAt": "2027-03-01",
              "physicalFitnessIssuedAt": "2026-03-01",
              "physicalFitnessNotes": null,
              "updatedAt": "2026-03-01T00:00:00Z"
            }
            """;
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var result = await BuildStructureClient(handler).GetMedicalRecordContextAsync(
            AthleteId, HabitsMedicalContextRules.MedicalRecordJustification);

        Assert.Equal(StructureMedicalRecordAvailability.Available, result.Availability);
        Assert.True(result.HasPhysicalFitnessCertificate);
        Assert.Equal("Sin alergias", result.Pathologies);
        Assert.Equal(new DateOnly(2027, 3, 1), result.PhysicalFitnessExpiresAt);
        // Estructura aún no publica campos estructurados: se consumen como "sin dato", nunca como false.
        Assert.Null(result.HasKnownAllergies);
        Assert.Null(result.HasKnownConditions);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(
            $"http://estructura:8080/api/v1/athletes/{AthleteId}/medical-record/access",
            handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("justification", handler.LastBody!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KAN-494", handler.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MedicalClient_WhenStructuredAllergyFieldsExist_ReadsThem()
    {
        var json = $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "hasPhysicalFitnessCertificate": true,
              "pathologies": null,
              "medicalHistory": null,
              "surgeries": null,
              "hasKnownAllergies": true,
              "hasKnownConditions": false
            }
            """;
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var result = await BuildStructureClient(handler).GetMedicalRecordContextAsync(
            AthleteId, HabitsMedicalContextRules.MedicalRecordJustification);

        Assert.True(result.HasKnownAllergies);
        Assert.False(result.HasKnownConditions);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task MedicalClient_WhenEndpointFails_ReturnsUnavailable(HttpStatusCode statusCode)
    {
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(statusCode));

        var result = await BuildStructureClient(handler).GetMedicalRecordContextAsync(
            AthleteId, HabitsMedicalContextRules.MedicalRecordJustification);

        Assert.Equal(StructureMedicalRecordAvailability.Unavailable, result.Availability);
    }

    [Fact]
    public async Task MedicalClient_WhenResponseHasNoRecord_ReturnsNoRecord()
    {
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":null,"hasPhysicalFitnessCertificate":false}""", Encoding.UTF8, "application/json")
        });

        var result = await BuildStructureClient(handler).GetMedicalRecordContextAsync(
            AthleteId, HabitsMedicalContextRules.MedicalRecordJustification);

        Assert.Equal(StructureMedicalRecordAvailability.NoRecord, result.Availability);
    }

    [Fact]
    public async Task MedicalClient_WhenResponseIsEmpty_ReturnsUnavailable()
    {
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        });

        var result = await BuildStructureClient(handler).GetMedicalRecordContextAsync(
            AthleteId, HabitsMedicalContextRules.MedicalRecordJustification);

        Assert.Equal(StructureMedicalRecordAvailability.Unavailable, result.Availability);
    }

    [Fact]
    public async Task NoSourceProvider_DoesNotInventWeightOrGoal()
    {
        var provider = new Infrastructure.Services.Recommendations.NoSourceHabitsAthleteContextProvider();

        var context = await provider.GetAsync(AthleteId);

        Assert.Null(context);
    }

    [Fact]
    public async Task NoSourceTrainingDayProvider_DoesNotInventHeavyDay()
    {
        var provider = new Infrastructure.Services.Recommendations.NoSourceHabitsTrainingDayContextProvider();

        var isHeavy = await provider.IsHeavyTrainingDayAsync(AthleteId, ArgentinaTime.Today);

        Assert.Null(isHeavy);
    }

    [Fact]
    public async Task StructureClient_WhenProfileHasWeightAndGoal_ReadsThem()
    {
        var json = $$"""
            {
              "id": "{{AthleteId}}",
              "firstName": "Lucas",
              "lastName": "Fernandez",
              "fullName": "Lucas Fernandez",
              "email": "lucas@club.com",
              "phone": "+5491155551234",
              "active": true,
              "weightKg": 70,
              "athleticGoal": "Ganar masa muscular"
            }
            """;
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var result = await BuildStructureClient(handler).GetAthleteProfileAsync(AthleteId);

        Assert.NotNull(result);
        Assert.Equal(AthleteId, result.Id);
        Assert.Equal(70d, result.WeightKg);
        Assert.Equal("Ganar masa muscular", result.AthleticGoal);
        Assert.Equal($"/api/v1/athletes/{AthleteId}", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task StructureClient_WhenProfileIsNotFound_ReturnsNull()
    {
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await BuildStructureClient(handler).GetAthleteProfileAsync(AthleteId);

        Assert.Null(result);
    }

    [Fact]
    public async Task StructureProvider_MapsCanonicalProfileToNutritionContext()
    {
        var client = new Mock<IStructureAthleteClient>();
        client.Setup(c => c.GetAthleteProfileAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteProfileDto(AthleteId, 70d, "Ganar masa muscular"));
        var provider = new Infrastructure.Services.Recommendations.StructureHabitsAthleteContextProvider(client.Object);

        var context = await provider.GetAsync(AthleteId);

        Assert.NotNull(context);
        Assert.Equal(70d, context.WeightKg);
        Assert.Equal("Ganar masa muscular", context.AthleticGoal);
    }

    [Fact]
    public async Task StructureProvider_WhenProfileIsMissing_ReturnsNullInsteadOfInventingValues()
    {
        var client = new Mock<IStructureAthleteClient>();
        client.Setup(c => c.GetAthleteProfileAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureAthleteProfileDto?)null);
        var provider = new Infrastructure.Services.Recommendations.StructureHabitsAthleteContextProvider(client.Object);

        var context = await provider.GetAsync(AthleteId);

        Assert.Null(context);
    }

    [Fact]
    public async Task StructureProvider_WhenStructureFails_ReturnsNullInsteadOfInventingValues()
    {
        var client = new Mock<IStructureAthleteClient>();
        client.Setup(c => c.GetAthleteProfileAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("Estructura no responde"));
        var provider = new Infrastructure.Services.Recommendations.StructureHabitsAthleteContextProvider(client.Object);

        var context = await provider.GetAsync(AthleteId);

        Assert.Null(context);
    }

    #endregion
}
