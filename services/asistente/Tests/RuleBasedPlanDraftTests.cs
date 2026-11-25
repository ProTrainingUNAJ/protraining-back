using Application.Common.Ai;
using Application.Common.PlanDrafts;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.DTOs.DayConflicts;
using Application.DTOs.PlanDrafts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
using Domain.Entities;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas unitarias y de aceptación para el motor de reglas de respaldo de borradores (KAN-605):
/// objetivos deportivos, progresión semanal y descarga, semanas con partido, reparto equilibrado,
/// foco muscular compartido, restricciones por molestias activas del atleta y validación de catálogo.
/// </summary>
public sealed class RuleBasedPlanDraftTests
{
    private static readonly Guid SquatId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LegPressId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid HipThrustId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid LungesId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid LegCurlId = Guid.Parse("10000000-0000-0000-0000-000000000005");
    private static readonly Guid CalfRaiseId = Guid.Parse("10000000-0000-0000-0000-000000000006");

    private static readonly Guid BenchPressId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OverheadPressId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid DipsId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid LateralRaiseId = Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid TricepPushdownId = Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid ChestFlyId = Guid.Parse("20000000-0000-0000-0000-000000000006");

    private static readonly Guid BarbellRowId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid PullUpId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid LatPulldownId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid FacePullId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid BicepCurlId = Guid.Parse("30000000-0000-0000-0000-000000000005");
    private static readonly Guid CableRowId = Guid.Parse("30000000-0000-0000-0000-000000000006");

    private static readonly Guid PlankId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    private static List<CatalogExerciseDto> BuildRichCatalog() =>
    [
        // Accesorios primero alfabéticamente para comprobar que el motor prioriza compuestos al inicio
        new(CalfRaiseId, "Elevación de gemelos", "Piernas", "Máquina"),
        new(LegCurlId, "Curl femoral acostado", "Piernas", "Máquina"),
        new(SquatId, "Sentadilla con barra", "Piernas", "Barra"),
        new(LegPressId, "Prensa de piernas", "Piernas", "Máquina"),
        new(HipThrustId, "Hip thrust con barra", "Piernas", "Barra"),
        new(LungesId, "Zancadas con mancuernas", "Piernas", "Mancuernas"),

        new(ChestFlyId, "Aperturas con mancuernas", "Pecho", "Mancuernas"),
        new(LateralRaiseId, "Elevaciones laterales", "Hombros", "Mancuernas"),
        new(TricepPushdownId, "Extensión de tríceps en polea", "Tríceps", "Polea"),
        new(BenchPressId, "Press de banca plano con barra", "Pecho", "Barra"),
        new(OverheadPressId, "Press militar con barra", "Hombros", "Barra"),
        new(DipsId, "Fondos en paralelas", "Pecho", "Paralelas"),

        new(BicepCurlId, "Curl de bíceps con barra", "Bíceps", "Barra"),
        new(FacePullId, "Face pull en polea", "Espalda", "Polea"),
        new(BarbellRowId, "Remo con barra", "Espalda", "Barra"),
        new(PullUpId, "Dominadas pronas", "Espalda", "Barra fija"),
        new(LatPulldownId, "Jalón al pecho", "Espalda", "Polea"),
        new(CableRowId, "Remo en polea baja", "Espalda", "Polea"),

        new(PlankId, "Plancha frontal", "Core", "Ninguno")
    ];

    private static Mock<IExerciseCatalogService> CreateCatalogMock(IReadOnlyList<CatalogExerciseDto> catalog)
    {
        var map = catalog.ToDictionary(e => e.Id);
        var mock = new Mock<IExerciseCatalogService>();
        mock.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
        mock.Setup(c => c.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => map.GetValueOrDefault(id));
        return mock;
    }

    private static ActiveInjuryRemoteDto CreateActiveInjury(Guid athleteId, string zone, string description) =>
        new(
            Id: Guid.NewGuid(),
            AthleteId: athleteId,
            Zone: zone,
            Description: description,
            InjuryDate: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            EstimatedReturnDate: new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc),
            Status: "Active",
            ClearedAt: null,
            CreatedAt: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

    private static RuleBasedAiClient CreateRuleClient(
        IReadOnlyList<CatalogExerciseDto> catalog,
        ITrainingPlanServiceClient? trainingPlanClient = null)
    {
        var catalogMock = CreateCatalogMock(catalog);
        return new RuleBasedAiClient(
            Mock.Of<IAiAuditService>(),
            NullLogger<RuleBasedAiClient>.Instance,
            catalogMock.Object,
            trainingPlanClient);
    }

    [Fact]
    public async Task Given_StrengthAndHypertrophyGoals_When_GeneratingWithRules_Then_UsesDistinctSetsRepsAndPercentagesFromTable()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);

        var strengthPlan = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza máxima",
            CategoryId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        var hypertrophyPlan = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Hipertrofia y masa muscular",
            CategoryId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        Assert.NotNull(strengthPlan);
        Assert.NotNull(hypertrophyPlan);

        var strengthSession = strengthPlan!.Weeks[0].Sessions[0];
        var strengthMain = strengthSession.Exercises[0];
        var strengthAccessory = strengthSession.Exercises[2];

        // Fuerza (NSCA): principal 4x5 al 85% (tope 87,5%), accesorios (3.°+) 3x8 al 70%, descanso 3 min
        Assert.Equal(4, strengthMain.Sets);
        Assert.Equal(5, strengthMain.Reps);
        Assert.Equal(85m, strengthMain.TargetPercentage);
        Assert.Equal(3, strengthAccessory.Sets);
        Assert.Equal(8, strengthAccessory.Reps);
        Assert.Equal(70m, strengthAccessory.TargetPercentage);
        Assert.Contains("Objetivo: fuerza", strengthSession.CoachNotes);
        Assert.Contains("3 min", strengthSession.CoachNotes);

        var hypertrophySession = hypertrophyPlan!.Weeks[0].Sessions[0];
        var hypertrophyMain = hypertrophySession.Exercises[0];
        var hypertrophyAccessory = hypertrophySession.Exercises[2];

        // Hipertrofia (NSCA): principal 4x10 al 70%, accesorios (3.°+) 3x12 al 60%, descanso 90 s
        Assert.Equal(4, hypertrophyMain.Sets);
        Assert.Equal(10, hypertrophyMain.Reps);
        Assert.Equal(70m, hypertrophyMain.TargetPercentage);
        Assert.Equal(3, hypertrophyAccessory.Sets);
        Assert.Equal(12, hypertrophyAccessory.Reps);
        Assert.Equal(60m, hypertrophyAccessory.TargetPercentage);
        Assert.Contains("Objetivo: hipertrofia", hypertrophySession.CoachNotes);
        Assert.Contains("90 s", hypertrophySession.CoachNotes);

        Assert.NotEqual(strengthMain.Reps, hypertrophyMain.Reps);
        Assert.NotEqual(strengthMain.TargetPercentage, hypertrophyMain.TargetPercentage);
    }

    [Theory]
    [InlineData("MAXIMUM STRENGTH", RuleTrainingGoal.Strength)]
    [InlineData("Fuerza maxima", RuleTrainingGoal.Strength)]
    [InlineData("Fuerza muscular", RuleTrainingGoal.Strength)]
    [InlineData("Volumen e HIPERTROFIA", RuleTrainingGoal.Hypertrophy)]
    [InlineData("Desarrollo muscular", RuleTrainingGoal.Hypertrophy)]
    [InlineData("Masa muscular", RuleTrainingGoal.Hypertrophy)]
    [InlineData("Explosividad de piernas", RuleTrainingGoal.Power)]
    [InlineData("Speed & Power", RuleTrainingGoal.Power)]
    [InlineData("Resistencia aeróbica y tonificación", RuleTrainingGoal.Endurance)]
    [InlineData("Resistencia muscular", RuleTrainingGoal.Endurance)]
    [InlineData("Acondicionamiento base", RuleTrainingGoal.General)]
    public void DetectGoalProfile_HandlesSpanishEnglishAccentsAndCase(string rawGoal, RuleTrainingGoal expectedGoal)
    {
        var profile = RuleBasedGoalProfiles.Detect(rawGoal);
        Assert.Equal(expectedGoal, profile.Goal);
    }

    [Fact]
    public async Task Given_ExplosividadDePiernas_When_GeneratingWithRules_Then_DetectsPowerAndUsesOnlyLegExercises()
    {
        var catalog = BuildRichCatalog();
        var catalogById = catalog.ToDictionary(e => e.Id);
        var client = CreateRuleClient(catalog);

        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Explosividad de piernas",
            CategoryId: Guid.NewGuid(),
            Weeks: 2,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        Assert.NotNull(content);

        foreach (var session in content!.Weeks.SelectMany(w => w.Sessions))
        {
            Assert.Contains("Piernas", session.Name);
            Assert.Contains("Objetivo: potencia", session.CoachNotes);
            Assert.Contains("explosivo", session.CoachNotes);
            Assert.Contains("3 min", session.CoachNotes);

            Assert.All(session.Exercises, ex =>
            {
                Assert.True(ex.ExerciseId.HasValue);
                var catalogItem = catalogById[ex.ExerciseId!.Value];
                Assert.Equal("Piernas", catalogItem.MuscleGroup);
            });
        }

        // Potencia (NSCA principal 75-85%, Cormie et al. accesorio 45%):
        // Semana 1 principal: 4x3 al 75%, Semana 2 principal: 4x3 al 77,5%; accesorio 3x6 al 45%
        var week1Main = content.Weeks[0].Sessions[0].Exercises[0];
        var week1Accessory = content.Weeks[0].Sessions[0].Exercises[2];
        var week2Main = content.Weeks[1].Sessions[0].Exercises[0];
        Assert.Equal(4, week1Main.Sets);
        Assert.Equal(3, week1Main.Reps);
        Assert.Equal(75m, week1Main.TargetPercentage);
        Assert.Equal(77.5m, week2Main.TargetPercentage);
        Assert.Equal(3, week1Accessory.Sets);
        Assert.Equal(6, week1Accessory.Reps);
        Assert.Equal(45m, week1Accessory.TargetPercentage);
    }

    [Fact]
    public async Task Given_UpperBodyFocus_When_GeneratingWithRules_Then_UsesSharedMapperAndExcludesLegs()
    {
        var catalog = BuildRichCatalog();
        var catalogById = catalog.ToDictionary(e => e.Id);
        var client = CreateRuleClient(catalog);

        // Verifica que RoutinePromptBuilder y GoalMuscleFocusMapper devuelvan exactamente lo mismo
        Assert.Equal(
            GoalMuscleFocusMapper.ExtractMuscleGroups("Fuerza de tren superior"),
            RoutinePromptBuilder.ExtractMuscleGroups("Fuerza de tren superior"));

        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza de tren superior",
            CategoryId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        Assert.NotNull(content);
        Assert.All(content!.Weeks[0].Sessions.SelectMany(s => s.Exercises), ex =>
        {
            var item = catalogById[ex.ExerciseId!.Value];
            Assert.NotEqual("Piernas", item.MuscleGroup);
        });
    }

    [Fact]
    public async Task Given_ThreeWeekStrengthPlan_When_GeneratingWithRules_Then_MainPercentageIncreasesWeeklyWithoutDeload()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);

        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza, 3 semanas, 3 días",
            CategoryId: Guid.NewGuid(),
            Weeks: 3,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        Assert.NotNull(content);
        Assert.Equal(3, content!.Weeks.Count);

        var w1Main = content.Weeks[0].Sessions[0].Exercises[0];
        var w2Main = content.Weeks[1].Sessions[0].Exercises[0];
        var w3Main = content.Weeks[2].Sessions[0].Exercises[0];

        // Fuerza (NSCA): arranca en 85%, sube +2,5% a 87,5% en semana 2 y respeta el tope de 87,5% en semana 3
        Assert.Equal(85m, w1Main.TargetPercentage);
        Assert.Equal(87.5m, w2Main.TargetPercentage);
        Assert.Equal(87.5m, w3Main.TargetPercentage);
        Assert.Equal(4, w1Main.Sets);
        Assert.Equal(4, w2Main.Sets);
        Assert.Equal(4, w3Main.Sets);
    }

    [Fact]
    public async Task Given_FourOrSixWeekStrengthPlan_When_GeneratingWithRules_Then_RespectsGoalCapAndLastWeekIsDeload()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);

        var fourWeeks = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            CategoryId: Guid.NewGuid(),
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        Assert.NotNull(fourWeeks);
        Assert.Equal(4, fourWeeks!.Weeks.Count);

        // Semanas 1..3 suben 85 -> 87,5 -> 87,5 (tope); Semana 4 es descarga (-1 serie, -10 %RM desde base -> 3 series, 75%)
        Assert.Equal(85m, fourWeeks.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(87.5m, fourWeeks.Weeks[1].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(87.5m, fourWeeks.Weeks[2].Sessions[0].Exercises[0].TargetPercentage);

        var deloadMain = fourWeeks.Weeks[3].Sessions[0].Exercises[0];
        var deloadAccessory = fourWeeks.Weeks[3].Sessions[0].Exercises[2];
        Assert.Equal(3, deloadMain.Sets);
        Assert.Equal(75m, deloadMain.TargetPercentage);
        Assert.Equal(2, deloadAccessory.Sets);
        Assert.Equal(60m, deloadAccessory.TargetPercentage);
        Assert.Contains("descarga", fourWeeks.Weeks[3].Sessions[0].CoachNotes, StringComparison.OrdinalIgnoreCase);

        // En 6 semanas, semanas 2..5 no superan el tope del objetivo (87,5%) y semana 6 es descarga
        var sixWeeks = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            CategoryId: Guid.NewGuid(),
            Weeks: 6,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23)));

        Assert.NotNull(sixWeeks);
        Assert.Equal(87.5m, sixWeeks!.Weeks[2].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(87.5m, sixWeeks.Weeks[3].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(87.5m, sixWeeks.Weeks[4].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(3, sixWeeks.Weeks[5].Sessions[0].Exercises[0].Sets);
        Assert.Equal(75m, sixWeeks.Weeks[5].Sessions[0].Exercises[0].TargetPercentage);
    }

    [Fact]
    public async Task Given_MatchInWeekTwo_When_GeneratingWithRules_Then_WeekTwoHasReducedLoadAndMatchNote()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);

        // Inicio lunes 2026-11-23. Partido el domingo de la semana 2 (2026-12-06)
        var startDate = new DateOnly(2026, 11, 23);
        var week2MatchDate = new DateOnly(2026, 12, 6);

        var content = await client.TryGeneratePlanDraftAsync(
            new CreatePlanDraftRequestDto(
                Goal: "Fuerza",
                CategoryId: Guid.NewGuid(),
                Weeks: 3,
                DaysPerWeek: 3,
                StartDate: startDate),
            matchDates: [week2MatchDate]);

        Assert.NotNull(content);
        var week1Main = content!.Weeks[0].Sessions[0].Exercises[0];
        var week2Session = content.Weeks[1].Sessions[0];
        var week2Main = week2Session.Exercises[0];
        var week3Main = content.Weeks[2].Sessions[0].Exercises[0];

        // Semana 1 normal: 4x5 al 85%
        Assert.Equal(4, week1Main.Sets);
        Assert.Equal(85m, week1Main.TargetPercentage);
        Assert.DoesNotContain("Semana de partido: carga reducida", content.Weeks[0].Sessions[0].CoachNotes);

        // Semana 2 con partido: -1 serie (3) y -10 %RM (87,5 - 10 = 77,5%) + nota explícita
        Assert.Equal(3, week2Main.Sets);
        Assert.Equal(77.5m, week2Main.TargetPercentage);
        Assert.Contains("Semana de partido: carga reducida", week2Session.CoachNotes);

        // Semana 3 vuelve a carga normal en el tope del objetivo: 4x5 al 87,5%
        Assert.Equal(4, week3Main.Sets);
        Assert.Equal(87.5m, week3Main.TargetPercentage);
    }

    [Fact]
    public async Task Given_DaysPerWeekWithoutFocus_When_GeneratingWithRules_Then_AppliesExpectedBalancedSplits()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);
        var startDate = new DateOnly(2026, 11, 23);

        // 2 días -> Cuerpo completo A / Cuerpo completo B
        var twoDays = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza", CategoryId: Guid.NewGuid(), Weeks: 1, DaysPerWeek: 2, StartDate: startDate));
        Assert.Equal(
            ["Día 1 · Cuerpo completo A", "Día 2 · Cuerpo completo B"],
            twoDays!.Weeks[0].Sessions.Select(s => s.Name).ToList());

        // 3 días -> Empuje / Tracción / Piernas
        var threeDays = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza", CategoryId: Guid.NewGuid(), Weeks: 1, DaysPerWeek: 3, StartDate: startDate));
        Assert.Equal(
            ["Día 1 · Empuje", "Día 2 · Tracción", "Día 3 · Piernas"],
            threeDays!.Weeks[0].Sessions.Select(s => s.Name).ToList());

        // 4 días -> Tren superior / Tren inferior / Tren superior / Tren inferior
        var fourDays = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza", CategoryId: Guid.NewGuid(), Weeks: 1, DaysPerWeek: 4, StartDate: startDate));
        Assert.Equal(
            ["Día 1 · Tren superior", "Día 2 · Tren inferior", "Día 3 · Tren superior", "Día 4 · Tren inferior"],
            fourDays!.Weeks[0].Sessions.Select(s => s.Name).ToList());

        // 5 días -> Empuje / Tracción / Piernas / Superior / Inferior
        var fiveDays = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza", CategoryId: Guid.NewGuid(), Weeks: 1, DaysPerWeek: 5, StartDate: startDate));
        Assert.Equal(
            ["Día 1 · Empuje", "Día 2 · Tracción", "Día 3 · Piernas", "Día 4 · Superior", "Día 5 · Inferior"],
            fiveDays!.Weeks[0].Sessions.Select(s => s.Name).ToList());
    }

    [Fact]
    public async Task Given_AthleteWithKneeInjury_When_GeneratingWithRules_Then_NeverUsesRestrictedExerciseAndExplainsSubstitution()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);
        var athleteId = Guid.NewGuid();

        var adaptationContext = new AthleteAdaptationContextDto(
            AthleteId: athleteId,
            ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Molestia rotuliana")],
            CurrentRecords: [],
            Restrictions:
            [
                new ExerciseRestrictionContextDto(
                    ExerciseId: SquatId,
                    Zone: "rodilla",
                    Reason: "Sobrecarga rotuliana",
                    Substitutes: [new AuthorizedSubstituteDto(LegPressId, "Prensa de piernas")])
            ]);

        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            AthleteId: athleteId,
            Weeks: 2,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23),
            AdaptationContext: adaptationContext));

        Assert.NotNull(content);

        // Sentadilla (restringida por rodilla) NUNCA aparece en ninguna semana ni sesión
        var allExercises = content!.Weeks.SelectMany(w => w.Sessions).SelectMany(s => s.Exercises).ToList();
        Assert.DoesNotContain(allExercises, e => e.ExerciseId == SquatId);

        // La sesión de piernas usa el sustituto autorizado (Prensa de piernas), sin duplicados y con nota explicativa neutra
        var legsSession = content.Weeks[0].Sessions.Single(s => s.Name.Contains("Piernas"));
        Assert.Contains(legsSession.Exercises, e => e.ExerciseId == LegPressId);
        Assert.Equal(legsSession.Exercises.Count, legsSession.Exercises.Select(e => e.ExerciseId).Distinct().Count());
        Assert.Contains("Se reemplazó Sentadilla con barra por Prensa de piernas por la molestia de rodilla.", legsSession.CoachNotes);
    }

    [Fact]
    public async Task Given_AthleteWithActiveInjuryAndNoPreloadedRestrictions_When_GeneratingWithRules_Then_ChecksTrainingServiceAndSubstitutes()
    {
        var catalog = BuildRichCatalog();
        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();
        trainingClientMock
            .Setup(t => t.CheckExerciseRestrictionsAsync(
                It.Is<string>(z => z.Contains("rodilla", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ExerciseRestrictionCheckResultRemoteDto(
                    ExerciseId: SquatId,
                    Restricted: true,
                    Zone: "rodilla",
                    PainLevel: 3,
                    Reason: "Estrés rotuliano",
                    Substitutes: [new DayConflictSubstituteDto(LegPressId, "Prensa de piernas")])
            ]);

        var client = CreateRuleClient(catalog, trainingClientMock.Object);
        var athleteId = Guid.NewGuid();

        var adaptationContext = new AthleteAdaptationContextDto(
            AthleteId: athleteId,
            ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Dolor al bajar")],
            CurrentRecords: [],
            Restrictions: []);

        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza de piernas",
            AthleteId: athleteId,
            Weeks: 1,
            DaysPerWeek: 2,
            StartDate: new DateOnly(2026, 11, 23),
            AdaptationContext: adaptationContext));

        Assert.NotNull(content);
        var allExercises = content!.Weeks.SelectMany(w => w.Sessions).SelectMany(s => s.Exercises).ToList();
        Assert.DoesNotContain(allExercises, e => e.ExerciseId == SquatId);
        Assert.Contains(allExercises, e => e.ExerciseId == LegPressId);
        Assert.Contains("por la molestia de rodilla", content.Weeks[0].Sessions[0].CoachNotes);
        trainingClientMock.Verify(
            t => t.CheckExerciseRestrictionsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_CategoryDraft_When_GeneratingWithRules_Then_DoesNotApplyAthleteRestrictions()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);

        var otherAthleteId = Guid.NewGuid();
        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            CategoryId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23),
            AdaptationContext: new AthleteAdaptationContextDto(
                AthleteId: otherAthleteId,
                ActiveInjuries: [CreateActiveInjury(otherAthleteId, "rodilla", "Molestia")],
                CurrentRecords: [],
                Restrictions:
                [
                    new ExerciseRestrictionContextDto(
                        SquatId,
                        "rodilla",
                        "Molestia",
                        [new AuthorizedSubstituteDto(LegPressId, "Prensa de piernas")])
                ])));

        Assert.NotNull(content);
        var legsSession = content!.Weeks[0].Sessions.Single(s => s.Name.Contains("Piernas"));
        Assert.Contains(legsSession.Exercises, e => e.ExerciseId == SquatId);
        Assert.DoesNotContain("molestia de rodilla", legsSession.CoachNotes);
    }

    [Fact]
    public async Task Given_AnyRuleGeneratedDraft_When_SavedThroughHandlerAndValidated_Then_PassesPlanDraftContentValidatorAndKeepsProviderWarning()
    {
        var catalog = BuildRichCatalog();
        var catalogIds = catalog.Select(e => e.Id).ToHashSet();
        var catalogMock = CreateCatalogMock(catalog);
        var ruleClient = new RuleBasedAiClient(
            Mock.Of<IAiAuditService>(),
            NullLogger<RuleBasedAiClient>.Instance,
            catalogMock.Object);

        var resilientAi = new ResilientAiService(
            providers: [ruleClient],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: catalogMock.Object,
            logger: NullLogger<ResilientAiService>.Instance);

        var savedDrafts = new List<PlanDraft>();
        var repoMock = new Mock<IPlanDraftRepository>();
        repoMock.Setup(r => r.AddAsync(It.IsAny<PlanDraft>(), It.IsAny<CancellationToken>()))
            .Callback<PlanDraft, CancellationToken>((d, _) => savedDrafts.Add(d))
            .Returns(Task.CompletedTask);

        var handler = new CreatePlanDraftCommandHandler(
            resilientAi,
            repoMock.Object,
            Mock.Of<IUnitOfWork>(),
            catalogService: catalogMock.Object);

        // Criterio de aceptación KAN-605 (tabla NSCA):
        // DADO un PF sin IA disponible CUANDO pide "fuerza, 3 semanas, 3 días"
        // ENTONCES recibe empuje, tracción y piernas, con 4x5 al 85% que sube a 87,5% (tope) y notas claras
        var response = await handler.HandleAsync(new CreatePlanDraftCommand(new CreatePlanDraftRequestDto(
            Goal: "fuerza, 3 semanas, 3 días",
            CategoryId: Guid.NewGuid(),
            Weeks: 3,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23))));

        Assert.Equal("Reglas", response.Provider);
        Assert.Equal("Draft", response.Status);
        Assert.Single(savedDrafts);
        Assert.NotNull(response.Warnings);
        Assert.Contains(response.Warnings!, w => w.Contains("reglas fijas de ProTraining", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("reparto empuje, tracción y piernas", response.JustificationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("+2,5 %RM", response.JustificationSummary);

        var deserializedContent = PlanDraftMapper.DeserializeContent(savedDrafts[0].Content);
        var validatedNames = await PlanDraftContentValidator.ValidateAsync(deserializedContent, catalogMock.Object);
        Assert.NotEmpty(validatedNames);

        Assert.Equal(
            ["Día 1 · Empuje", "Día 2 · Tracción", "Día 3 · Piernas"],
            response.Weeks[0].Sessions.Select(s => s.Name).ToList());

        Assert.Equal(85m, response.Weeks[0].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(87.5m, response.Weeks[1].Sessions[0].Exercises[0].TargetPercentage);
        Assert.Equal(87.5m, response.Weeks[2].Sessions[0].Exercises[0].TargetPercentage);

        foreach (var session in response.Weeks.SelectMany(w => w.Sessions))
        {
            Assert.StartsWith("Entrada en calor (10 min)", session.CoachNotes);
            Assert.Contains("Armado por reglas (sin IA disponible)", session.CoachNotes);
            Assert.All(session.Exercises, e =>
            {
                Assert.True(e.ExerciseId.HasValue);
                Assert.Contains(e.ExerciseId!.Value, catalogIds);
            });
        }
    }

    [Fact]
    public async Task Given_EnduranceAndGeneralGoals_When_GeneratingWithRules_Then_UsesTableValuesForSetsRepsPercentageAndRest()
    {
        var catalog = BuildRichCatalog();
        var client = CreateRuleClient(catalog);
        var startDate = new DateOnly(2026, 11, 23);

        var endurancePlan = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Resistencia muscular",
            CategoryId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: startDate));

        var generalPlan = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Acondicionamiento base",
            CategoryId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 3,
            StartDate: startDate));

        Assert.NotNull(endurancePlan);
        Assert.NotNull(generalPlan);

        var enduranceSession = endurancePlan!.Weeks[0].Sessions[0];
        var enduranceMain = enduranceSession.Exercises[0];
        var enduranceAccessory = enduranceSession.Exercises[2];
        Assert.Equal(3, enduranceMain.Sets);
        Assert.Equal(15, enduranceMain.Reps);
        Assert.Equal(50m, enduranceMain.TargetPercentage);
        Assert.Equal(2, enduranceAccessory.Sets);
        Assert.Equal(18, enduranceAccessory.Reps);
        Assert.Equal(45m, enduranceAccessory.TargetPercentage);
        Assert.Contains("Objetivo: resistencia", enduranceSession.CoachNotes);
        Assert.Contains("30 s", enduranceSession.CoachNotes);

        var generalSession = generalPlan!.Weeks[0].Sessions[0];
        var generalMain = generalSession.Exercises[0];
        var generalAccessory = generalSession.Exercises[2];
        Assert.Equal(3, generalMain.Sets);
        Assert.Equal(10, generalMain.Reps);
        Assert.Equal(65m, generalMain.TargetPercentage);
        Assert.Equal(2, generalAccessory.Sets);
        Assert.Equal(12, generalAccessory.Reps);
        Assert.Equal(55m, generalAccessory.TargetPercentage);
        Assert.Contains("Objetivo: general", generalSession.CoachNotes);
        Assert.Contains("90 s", generalSession.CoachNotes);
    }

    [Fact]
    public async Task Given_TinyCatalogWhereAllGroupExercisesAreRestricted_When_GeneratingWithRules_Then_NeverEmitsRestrictedOrEmptySessionsAndPassesValidator()
    {
        // Catálogo mínimo: un único ejercicio de piernas (restringido sin sustitutos) y uno de pecho seguro
        List<CatalogExerciseDto> tinyCatalog =
        [
            new(SquatId, "Sentadilla con barra", "Piernas", "Barra"),
            new(BenchPressId, "Press de banca plano con barra", "Pecho", "Barra")
        ];
        var catalogMock = CreateCatalogMock(tinyCatalog);
        var client = new RuleBasedAiClient(
            Mock.Of<IAiAuditService>(),
            NullLogger<RuleBasedAiClient>.Instance,
            catalogMock.Object);

        var athleteId = Guid.NewGuid();
        var content = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            AthleteId: athleteId,
            Weeks: 2,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 23),
            AdaptationContext: new AthleteAdaptationContextDto(
                AthleteId: athleteId,
                ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Dolor agudo")],
                CurrentRecords: [],
                Restrictions:
                [
                    new ExerciseRestrictionContextDto(SquatId, "rodilla", "Restringido sin sustituto", [])
                ])));

        Assert.NotNull(content);
        var allSessions = content!.Weeks.SelectMany(w => w.Sessions).ToList();
        Assert.NotEmpty(allSessions);
        Assert.All(allSessions, s =>
        {
            Assert.NotEmpty(s.Exercises);
            Assert.DoesNotContain(s.Exercises, e => e.ExerciseId == SquatId);
        });

        var validated = await PlanDraftContentValidator.ValidateAsync(content, catalogMock.Object);
        Assert.Contains(BenchPressId, validated.Keys);
        Assert.DoesNotContain(SquatId, validated.Keys);
    }

    [Fact]
    public async Task Given_TrainingServiceUnavailableForInjuredAthlete_When_GeneratingWithRules_Then_AddsWarningNoteInDraftAndSessions()
    {
        var catalog = BuildRichCatalog();
        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();
        trainingClientMock
            .Setup(t => t.CheckExerciseRestrictionsAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Entrenamiento no responde"));

        var client = CreateRuleClient(catalog, trainingClientMock.Object);
        var athleteId = Guid.NewGuid();

        // Caso 1: sin restricciones precargadas, falla la consulta a Entrenamiento
        var withoutPreloaded = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza de piernas",
            AthleteId: athleteId,
            Weeks: 1,
            DaysPerWeek: 2,
            StartDate: new DateOnly(2026, 11, 23),
            AdaptationContext: new AthleteAdaptationContextDto(
                AthleteId: athleteId,
                ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Molestia")],
                CurrentRecords: [],
                Restrictions: [])));

        Assert.NotNull(withoutPreloaded);
        Assert.Contains("no se pudo consultar el servicio de Entrenamiento", withoutPreloaded!.JustificationSummary);
        Assert.All(withoutPreloaded.Weeks.SelectMany(w => w.Sessions), s =>
            Assert.Contains("no se pudo consultar el servicio de Entrenamiento", s.CoachNotes));

        // Caso 2: restricción precargada sin sustituto autorizado (usa alternativa del mismo grupo) y Entrenamiento caído
        var withFallback = await client.TryGeneratePlanDraftAsync(new CreatePlanDraftRequestDto(
            Goal: "Fuerza de tren superior",
            AthleteId: athleteId,
            Weeks: 1,
            DaysPerWeek: 2,
            StartDate: new DateOnly(2026, 11, 23),
            AdaptationContext: new AthleteAdaptationContextDto(
                AthleteId: athleteId,
                ActiveInjuries: [CreateActiveInjury(athleteId, "hombro", "Dolor anterior")],
                CurrentRecords: [],
                Restrictions:
                [
                    new ExerciseRestrictionContextDto(BenchPressId, "hombro", "Evitar press plano", [])
                ])));

        Assert.NotNull(withFallback);
        Assert.Contains("no se pudo consultar el servicio de Entrenamiento", withFallback!.JustificationSummary);
        var sessionWithReplacement = withFallback.Weeks[0].Sessions
            .First(s => (s.CoachNotes ?? string.Empty).Contains("Press de banca plano con barra", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(sessionWithReplacement.CoachNotes);
        Assert.Contains("Se reemplazó Press de banca plano con barra por", sessionWithReplacement.CoachNotes);
        Assert.DoesNotContain("cambiada", sessionWithReplacement.CoachNotes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sin validación de Entrenamiento", sessionWithReplacement.CoachNotes);
    }

    [Fact]
    public void Given_PreMatchTaperingWithNonSpanishCulture_When_GeneratingWeeks_Then_FormatsPercentageWithEsArCulture()
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

            var blueprint = new RuleSessionBlueprint(
                FocusName: "Empuje",
                Exercises: [new CatalogExerciseDto(BenchPressId, "Press de banca plano con barra", "Pecho", "Barra")],
                FallbackPool: [],
                SubstitutionNotes: []);

            var profile = RuleBasedGoalProfiles.Profiles[RuleTrainingGoal.Strength];
            // Semana 1: partido el martes 24 (lunes 23 es D-1) y el viernes 27 (miércoles 25 es D-2).
            // Semana 2 (sin partido): fuerza progresa a 87,5 %, lo que valida la coma decimal es-AR aun bajo en-US.
            var weeks = RuleBasedPlanProgression.GenerateWeeks(
                sessionBlueprints: [blueprint, blueprint],
                trainingDays: [DayOfWeek.Monday, DayOfWeek.Wednesday],
                profile: profile,
                totalWeeks: 2,
                startDate: new DateOnly(2026, 11, 23),
                matchDates: [new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 27)]);

            var mondayNotes = weeks[0].Sessions[0].CoachNotes;
            var wednesdayNotes = weeks[0].Sessions[1].CoachNotes;
            var week2Notes = weeks[1].Sessions[0].CoachNotes;

            Assert.Contains("Tapering D-1: carga limitada al 60 % por partido oficial.", mondayNotes);
            Assert.Contains("Tapering D-2: carga moderada al 75 % por proximidad de partido.", wednesdayNotes);
            Assert.Contains("al 87,5 %", week2Notes);
            Assert.DoesNotContain("87.5", week2Notes);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
