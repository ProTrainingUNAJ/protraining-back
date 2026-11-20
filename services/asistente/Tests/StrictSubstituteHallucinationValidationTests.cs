namespace Asistente.Tests;

using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Adaptation;
using Application.DTOs.Ai;
using Application.DTOs.DayConflicts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Domain.Entities;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas automatizadas de validación estricta en C# contra alucinaciones de sustitutos
/// y violaciones a la matriz biomecánica oficial de restricciones (#171 · KAN-466).
/// </summary>
public sealed class StrictSubstituteHallucinationValidationTests
{
    private static readonly Guid SquatId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BackSquatId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid BenchPressId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ConventionalDeadliftId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RomanianDeadliftId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BoxJumpId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid LegPressId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid LegPress45Id = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid BandSquatId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid GluteBridgeId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid BarbellRowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly IReadOnlyList<CatalogExerciseDto> OfficialCatalog =
    [
        new(SquatId, "Sentadilla con barra", "Piernas", "Barra"),
        new(BackSquatId, "Sentadilla trasera con barra", "Piernas", "Barra"),
        new(BenchPressId, "Press de banca plano con barra", "Pecho", "Barra"),
        new(ConventionalDeadliftId, "Peso muerto convencional", "Espalda", "Barra"),
        new(RomanianDeadliftId, "Peso muerto rumano", "Piernas", "Barra"),
        new(BoxJumpId, "Salto al cajón (respuesta múltiple)", "Piernas", "Cajón"),
        new(LegPressId, "Prensa de piernas", "Piernas", "Máquina"),
        new(LegPress45Id, "Prensa de piernas 45°", "Piernas", "Máquina"),
        new(BandSquatId, "Sentadillas con bandas elásticas", "Piernas", "Banda"),
        new(GluteBridgeId, "Puente de glúteos con barra", "Piernas", "Barra"),
        new(BarbellRowId, "Remo con barra", "Espalda", "Barra")
    ];

    private static readonly IReadOnlyList<AuthorizedSubstituteDto> KneeAuthorizedSubstitutes =
    [
        new(LegPressId, "Prensa de piernas"),
        new(BandSquatId, "Sentadillas con bandas elásticas"),
        new(GluteBridgeId, "Puente de glúteos con barra")
    ];

    private static readonly IReadOnlyList<AuthorizedSubstituteDto> LumbarAuthorizedSubstitutes =
    [
        new(GluteBridgeId, "Puente de glúteos con barra"),
        new(LegPressId, "Prensa de piernas")
    ];

    private static Mock<IExerciseCatalogService> CreateCatalogMock()
    {
        var mock = new Mock<IExerciseCatalogService>();

        mock.Setup(c => c.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => OfficialCatalog.FirstOrDefault(e => e.Id == id));

        mock.Setup(c => c.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                var normalized = name.Trim();
                return OfficialCatalog.FirstOrDefault(e =>
                    string.Equals(e.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(e.Name, StringComparison.OrdinalIgnoreCase));
            });

        mock.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OfficialCatalog);

        return mock;
    }

    private static ActiveInjuryRemoteDto CreateActiveInjury(Guid athleteId, string zone, string description) =>
        new(
            Id: Guid.NewGuid(),
            AthleteId: athleteId,
            Zone: zone,
            Description: description,
            InjuryDate: DateTime.UtcNow.AddDays(-5),
            EstimatedReturnDate: DateTime.UtcNow.AddDays(14),
            Status: "Active",
            ClearedAt: null,
            CreatedAt: DateTime.UtcNow.AddDays(-5));

    private static AthleteAdaptationContextDto CreateKneeInjuryContext(Guid athleteId) =>
        new(
            AthleteId: athleteId,
            ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Dolor en tendón rotuliano")],
            CurrentRecords:
            [
                new AthleteRecordRemoteDto(
                    Guid.NewGuid(),
                    athleteId,
                    SquatId,
                    "Sentadilla con barra",
                    110m,
                    "Tested",
                    DateTime.UtcNow.AddDays(-10),
                    true)
            ],
            Restrictions:
            [
                new ExerciseRestrictionContextDto(
                    SquatId,
                    "rodilla",
                    "Alta compresión femororrotuliana y tensión en el tendón rotuliano",
                    KneeAuthorizedSubstitutes)
            ]);

    [Fact]
    public async Task Given_LlmInjectsDangerousInventedExercise_When_ValidatingSingleExerciseAdaptation_Then_ReplacesWithOfficialDbSubstitute()
    {
        var athleteId = Guid.NewGuid();
        var fakeHallucinatedId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);
        var context = CreateKneeInjuryContext(athleteId);

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Fuerza segura de tren inferior",
            JustificationSummary: "Propuesta con alucinación peligrosa",
            Exercises:
            [
                new SuggestedExerciseDto(
                    "Sentadilla sissy pliométrica con salto y barra",
                    Sets: 4,
                    Reps: 6,
                    SuggestedPercentage: 70m,
                    ExerciseId: fakeHallucinatedId)
            ],
            ProviderUsed: "Agente (OpenAI)");

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: context);

        Assert.True(result.IsValid);
        Assert.False(result.RequiresDeterministicFallback);
        Assert.True(result.HasCorrections);
        Assert.True(result.SanitizedRoutine.WasAdapted);

        var delivered = Assert.Single(result.SanitizedRoutine.Exercises);
        Assert.Equal(LegPressId, delivered.ExerciseId);
        Assert.Equal("Prensa de piernas", delivered.ExerciseName);
        Assert.False(delivered.IsReference);

        var authorizedIds = KneeAuthorizedSubstitutes.Select(s => s.ExerciseId).ToHashSet();
        Assert.Contains(delivered.ExerciseId!.Value, authorizedIds);

        var change = Assert.Single(result.SanitizedRoutine.AdaptationChanges!);
        Assert.Equal(fakeHallucinatedId, change.OriginalExerciseId);
        Assert.Equal("Sentadilla sissy pliométrica con salto y barra", change.OriginalExerciseName);
        Assert.Equal(LegPressId, change.FinalExerciseId);
        Assert.Equal("Prensa de piernas", change.FinalExerciseName);
        Assert.Contains(result.SanitizedRoutine.AdaptationWarnings!, w => w.Contains("sustituto autorizado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Given_LlmInjectsDangerousInventedSubstituteInMultiExerciseSession_When_ValidatingAdaptation_Then_SanitizesHallucinationToDbSubstituteAndPreservesSafeExercises()
    {
        var athleteId = Guid.NewGuid();
        var fakeHallucinatedId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);
        var context = CreateKneeInjuryContext(athleteId);

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Sesión mixta adaptada",
            JustificationSummary: "Fuerza general con lesión de rodilla",
            Exercises:
            [
                new SuggestedExerciseDto("Salto profundo a una pierna con barra", 4, 5, 75m, fakeHallucinatedId),
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 75m, BenchPressId)
            ],
            ProviderUsed: "Agente (Gemini)");

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: context);

        Assert.True(result.IsValid);
        Assert.False(result.RequiresDeterministicFallback);
        Assert.True(result.SanitizedRoutine.WasAdapted);
        Assert.Equal(2, result.SanitizedRoutine.Exercises.Count);

        var first = result.SanitizedRoutine.Exercises[0];
        Assert.Equal(LegPressId, first.ExerciseId);
        Assert.Equal("Prensa de piernas", first.ExerciseName);
        Assert.False(first.IsReference);

        var second = result.SanitizedRoutine.Exercises[1];
        Assert.Equal(BenchPressId, second.ExerciseId);
        Assert.Equal("Press de banca plano con barra", second.ExerciseName);
        Assert.False(second.IsReference);

        Assert.All(result.SanitizedRoutine.Exercises, e =>
        {
            Assert.NotNull(e.ExerciseId);
            Assert.NotEqual(Guid.Empty, e.ExerciseId);
            Assert.False(e.IsReference);
        });
    }

    [Fact]
    public async Task Given_LlmSubstitutesBaseRestrictedExerciseWithUnauthorizedCatalogExercise_When_ValidatingAdaptation_Then_EnforcesAuthorizedDbSubstitute()
    {
        var athleteId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);
        var context = CreateKneeInjuryContext(athleteId);

        var baseExercises = new List<SuggestedExerciseDto>
        {
            new("Sentadilla con barra", 4, 5, 75m, SquatId),
            new("Press de banca plano con barra", 4, 6, 70m, BenchPressId)
        };

        // El LLM intenta sustituir Sentadilla con barra por Peso muerto convencional (del catálogo, pero NO autorizado para esta restricción)
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Adaptación de rutina",
            JustificationSummary: "Intento de sustitución fuera de matriz",
            Exercises:
            [
                new SuggestedExerciseDto("Peso muerto convencional", 4, 5, 75m, ConventionalDeadliftId),
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 70m, BenchPressId)
            ],
            ProviderUsed: "Agente (OpenAI)",
            BaseExercises: baseExercises);

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: context);

        Assert.True(result.IsValid);
        Assert.True(result.SanitizedRoutine.WasAdapted);

        var corrected = result.SanitizedRoutine.Exercises[0];
        Assert.Equal(LegPressId, corrected.ExerciseId);
        Assert.Equal("Prensa de piernas", corrected.ExerciseName);
        Assert.False(corrected.IsReference);

        var untouched = result.SanitizedRoutine.Exercises[1];
        Assert.Equal(BenchPressId, untouched.ExerciseId);

        var change = Assert.Single(result.SanitizedRoutine.AdaptationChanges!);
        Assert.Equal(SquatId, change.OriginalExerciseId);
        Assert.Equal(LegPressId, change.FinalExerciseId);
        Assert.Contains("'Peso muerto convencional' no estaba autorizado", change.Reason);
    }

    [Fact]
    public async Task Given_LlmIgnoresRestrictionsAndReturnsMultipleContraindicatedExercises_When_ValidatingAdaptation_Then_ReplacesEveryRestrictedExerciseWithDbSubstitute()
    {
        var athleteId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);

        var context = new AthleteAdaptationContextDto(
            AthleteId: athleteId,
            ActiveInjuries:
            [
                CreateActiveInjury(athleteId, "rodilla", "Dolor rotuliano"),
                CreateActiveInjury(athleteId, "espalda_baja", "Sobrecarga lumbar")
            ],
            CurrentRecords: [],
            Restrictions:
            [
                new ExerciseRestrictionContextDto(
                    SquatId,
                    "rodilla",
                    "Reduce estrés en tendón rotuliano",
                    KneeAuthorizedSubstitutes),
                new ExerciseRestrictionContextDto(
                    RomanianDeadliftId,
                    "espalda_baja",
                    "Evita cizallamiento lumbar bajo carga",
                    LumbarAuthorizedSubstitutes)
            ]);

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Fuerza general",
            JustificationSummary: "El LLM ignoró ambas restricciones",
            Exercises:
            [
                new SuggestedExerciseDto("Sentadilla con barra", 4, 5, 80m, SquatId),
                new SuggestedExerciseDto("Peso muerto rumano", 3, 8, 70m, RomanianDeadliftId),
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 75m, BenchPressId)
            ],
            ProviderUsed: "Agente (OpenAI)");

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: context);

        Assert.True(result.IsValid);
        Assert.True(result.SanitizedRoutine.WasAdapted);
        Assert.Equal(3, result.SanitizedRoutine.Exercises.Count);

        Assert.Equal(LegPressId, result.SanitizedRoutine.Exercises[0].ExerciseId);
        Assert.Equal("Prensa de piernas", result.SanitizedRoutine.Exercises[0].ExerciseName);

        Assert.Equal(GluteBridgeId, result.SanitizedRoutine.Exercises[1].ExerciseId);
        Assert.Equal("Puente de glúteos con barra", result.SanitizedRoutine.Exercises[1].ExerciseName);

        Assert.Equal(BenchPressId, result.SanitizedRoutine.Exercises[2].ExerciseId);
        Assert.Equal(2, result.SanitizedRoutine.AdaptationChanges!.Count);
    }

    [Fact]
    public async Task Given_LlmProposesAuthorizedSecondSubstituteFromDb_When_ValidatingAdaptation_Then_AcceptsAuthorizedSubstituteWithoutOverriding()
    {
        var athleteId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);
        var context = CreateKneeInjuryContext(athleteId);

        var baseExercises = new List<SuggestedExerciseDto>
        {
            new("Sentadilla con barra", 4, 5, 75m, SquatId),
            new("Press de banca plano con barra", 4, 6, 70m, BenchPressId)
        };

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Fuerza adaptada",
            JustificationSummary: "Sustitución válida con segundo sustituto oficial",
            Exercises:
            [
                new SuggestedExerciseDto("Sentadillas con bandas elásticas", 3, 12, 50m, BandSquatId),
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 70m, BenchPressId)
            ],
            ProviderUsed: "Agente (OpenAI)",
            BaseExercises: baseExercises);

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: context);

        Assert.True(result.IsValid);
        Assert.False(result.RequiresDeterministicFallback);
        Assert.False(result.HasCorrections);
        Assert.False(result.SanitizedRoutine.WasAdapted);

        var first = result.SanitizedRoutine.Exercises[0];
        Assert.Equal(BandSquatId, first.ExerciseId);
        Assert.Equal("Sentadillas con bandas elásticas", first.ExerciseName);
        Assert.False(first.IsReference);

        Assert.Equal(BenchPressId, result.SanitizedRoutine.Exercises[1].ExerciseId);
        Assert.Empty(result.SanitizedRoutine.AdaptationChanges!);
    }

    [Fact]
    public async Task Given_LlmHallucinatesDangerousExerciseInFullPipeline_When_ResilientAiServiceAndUseCaseExecuteAdaptation_Then_ClientReceives100PercentDbAuthorizedSubstitute()
    {
        var athleteId = Guid.NewGuid();
        var fakeHallucinatedId = Guid.NewGuid();

        // Respuesta cruda del LLM parseada con el parser oficial
        var rawLlmJson = $$"""
        {
          "tituloPropuesto": "Fuerza de piernas adaptada",
          "resumenJustificacion": "Sesión adaptada por molestia en rodilla.",
          "ejercicios": [
            {
              "ejercicioId": "{{fakeHallucinatedId}}",
              "ejercicioNombre": "Sentadilla aérea explosiva con barra olímpica",
              "series": 4,
              "repeticiones": 6,
              "porcentajeSugerido": 75.0
            },
            {
              "ejercicioId": "{{BenchPressId}}",
              "ejercicioNombre": "Press de banca plano con barra",
              "series": 4,
              "repeticiones": 6,
              "porcentajeSugerido": 70.0
            }
          ]
        }
        """;

        var parsed = AiRoutineParser.ParseAndValidate(rawLlmJson);
        Assert.True(parsed.IsValid);

        var llmClientMock = new Mock<IAiClient>();
        llmClientMock.Setup(c => c.ProviderName).Returns("Agente (OpenAI)");
        llmClientMock
            .Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) => new RoutineSuggestionResponseDto(
                AthleteId: req.AthleteId,
                ProposedTitle: parsed.ProposedTitle!,
                JustificationSummary: parsed.JustificationSummary!,
                Exercises: parsed.Exercises.ToList(),
                ProviderUsed: "Agente (OpenAI)"));

        var catalogMock = CreateCatalogMock();
        var validator = new ExerciseSemanticValidator(catalogMock.Object);
        var auditMock = new Mock<IAiAuditService>();
        var rulesClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var aiService = new ResilientAiService(
            providers: [llmClientMock.Object, rulesClient],
            semanticValidator: validator,
            responseCache: null,
            currentUser: null,
            auditService: auditMock.Object,
            catalogService: catalogMock.Object,
            logger: NullLogger<ResilientAiService>.Instance);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock
            .Setup(c => c.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var contextMock = new Mock<IAthleteAdaptationContextService>();
        contextMock
            .Setup(c => c.GetAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateKneeInjuryContext(athleteId));

        RoutineDraft? persistedDraft = null;
        var draftRepoMock = new Mock<IRoutineDraftRepository>();
        draftRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RoutineDraft>(), It.IsAny<CancellationToken>()))
            .Callback<RoutineDraft, CancellationToken>((draft, _) => persistedDraft = draft)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();

        var useCase = new SuggestRoutineUseCase(
            aiService: aiService,
            adaptationContextService: contextMock.Object,
            structureAthleteClient: structureMock.Object,
            routineDraftRepository: draftRepoMock.Object,
            unitOfWork: uowMock.Object);

        var response = await useCase.ExecuteAdaptationAsync(
            new RoutineSuggestionRequestDto(athleteId, "Fuerza de piernas"));

        var authorizedIds = KneeAuthorizedSubstitutes.Select(s => s.ExerciseId).ToHashSet();

        Assert.Equal("Agente (OpenAI)", response.ProviderUsed);
        Assert.True(response.WasAdapted);
        Assert.Equal(2, response.Exercises.Count);

        var adaptedExercise = response.Exercises[0];
        Assert.Equal(LegPressId, adaptedExercise.ExerciseId);
        Assert.Equal("Prensa de piernas", adaptedExercise.ExerciseName);
        Assert.Contains(adaptedExercise.ExerciseId!.Value, authorizedIds);
        Assert.False(adaptedExercise.IsReference);

        Assert.Equal(BenchPressId, response.Exercises[1].ExerciseId);

        // Verificar que lo persistido en BD tampoco contiene la alucinación
        Assert.NotNull(persistedDraft);
        var persistedExercises = JsonSerializer.Deserialize<List<SuggestedExerciseDto>>(persistedDraft!.ExercisesJson)!;
        Assert.Equal(2, persistedExercises.Count);
        Assert.Equal(LegPressId, persistedExercises[0].ExerciseId);
        Assert.Equal("Prensa de piernas", persistedExercises[0].ExerciseName);
        Assert.False(persistedExercises[0].IsReference);
    }

    [Fact]
    public async Task Given_LlmProposesDifferentContraindicatedExerciseNotInAthleteRecords_When_ExecuteAdaptationRuns_Then_PostCheckQueriesRestrictionsAndReplacesWithDbSubstitute()
    {
        var athleteId = Guid.NewGuid();

        var llmClientMock = new Mock<IAiClient>();
        llmClientMock.Setup(c => c.ProviderName).Returns("Agente (OpenAI)");
        llmClientMock
            .Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) => new RoutineSuggestionResponseDto(
                AthleteId: req.AthleteId,
                ProposedTitle: "Potencia y tren superior",
                JustificationSummary: "Trabajo pliométrico y empuje",
                Exercises:
                [
                    new SuggestedExerciseDto("Salto al cajón (respuesta múltiple)", 3, 8, 0m, BoxJumpId),
                    new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 75m, BenchPressId)
                ],
                ProviderUsed: "Agente (OpenAI)"));

        var catalogMock = CreateCatalogMock();
        var validator = new ExerciseSemanticValidator(catalogMock.Object);
        var auditMock = new Mock<IAiAuditService>();
        var rulesClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var aiService = new ResilientAiService(
            providers: [llmClientMock.Object, rulesClient],
            semanticValidator: validator,
            responseCache: null,
            currentUser: null,
            auditService: auditMock.Object,
            catalogService: catalogMock.Object,
            logger: NullLogger<ResilientAiService>.Instance);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock
            .Setup(c => c.IsOwnAthleteAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // El atleta tiene lesión de rodilla y récord 1RM solo en Sentadilla con barra (no en Salto al cajón)
        var initialContext = CreateKneeInjuryContext(athleteId);
        var contextMock = new Mock<IAthleteAdaptationContextService>();
        contextMock
            .Setup(c => c.GetAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialContext);

        var trainingClientMock = new Mock<ITrainingPlanServiceClient>();
        trainingClientMock
            .Setup(c => c.CheckExerciseRestrictionsAsync(
                It.Is<string>(z => z.Contains("rodilla:3") && z.Contains("cuadriceps:3")),
                It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(BoxJumpId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExerciseRestrictionCheckResultRemoteDto(
                    BoxJumpId,
                    true,
                    "cuadriceps",
                    3,
                    "Impacto reactivo y desaceleración brusca que sobrecargan el tendón rotuliano.",
                    [
                        new DayConflictSubstituteDto(LegPressId, "Prensa de piernas"),
                        new DayConflictSubstituteDto(BandSquatId, "Sentadillas con bandas elásticas")
                    ])
            ]);

        RoutineDraft? persistedDraft = null;
        var draftRepoMock = new Mock<IRoutineDraftRepository>();
        draftRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RoutineDraft>(), It.IsAny<CancellationToken>()))
            .Callback<RoutineDraft, CancellationToken>((draft, _) => persistedDraft = draft)
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();

        var useCase = new SuggestRoutineUseCase(
            aiService: aiService,
            adaptationContextService: contextMock.Object,
            structureAthleteClient: structureMock.Object,
            routineDraftRepository: draftRepoMock.Object,
            unitOfWork: uowMock.Object,
            trainingPlanServiceClient: trainingClientMock.Object);

        var response = await useCase.ExecuteAdaptationAsync(
            new RoutineSuggestionRequestDto(athleteId, "Potencia y tren superior"));

        Assert.True(response.WasAdapted);
        Assert.Equal(2, response.Exercises.Count);
        Assert.Equal(LegPressId, response.Exercises[0].ExerciseId);
        Assert.Equal("Prensa de piernas", response.Exercises[0].ExerciseName);
        Assert.False(response.Exercises[0].IsReference);
        Assert.Equal(BenchPressId, response.Exercises[1].ExerciseId);

        Assert.DoesNotContain(response.Exercises, e => e.ExerciseId == BoxJumpId);
        var change = Assert.Single(response.AdaptationChanges!);
        Assert.Equal(BoxJumpId, change.OriginalExerciseId);
        Assert.Equal(LegPressId, change.FinalExerciseId);

        Assert.NotNull(persistedDraft);
        var persistedExercises = JsonSerializer.Deserialize<List<SuggestedExerciseDto>>(persistedDraft!.ExercisesJson)!;
        Assert.DoesNotContain(persistedExercises, e => e.ExerciseId == BoxJumpId);
        Assert.Equal(LegPressId, persistedExercises[0].ExerciseId);
    }

    [Fact]
    public async Task Given_PrimaryLlmHallucinatesSeverely_When_ResilientAiServiceFallsBackToDeterministicRules_Then_FinalOutputStillRespectsBiomechanicalRestrictions()
    {
        var athleteId = Guid.NewGuid();

        var corruptProviderMock = new Mock<IAiClient>();
        corruptProviderMock.Setup(p => p.ProviderName).Returns("Agente (OpenAI)");
        corruptProviderMock
            .Setup(p => p.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: athleteId,
                ProposedTitle: "Rutina alucinada",
                JustificationSummary: "Propuesta con ejercicios inexistentes",
                Exercises:
                [
                    new SuggestedExerciseDto("Salto dimensional 1", 3, 10, 0m, Guid.NewGuid()),
                    new SuggestedExerciseDto("Salto dimensional 2", 3, 10, 0m, Guid.NewGuid()),
                    new SuggestedExerciseDto("Salto dimensional 3", 3, 10, 0m, Guid.NewGuid())
                ],
                ProviderUsed: "Agente (OpenAI)"));

        var catalogMock = CreateCatalogMock();
        var validator = new ExerciseSemanticValidator(catalogMock.Object);
        var auditMock = new Mock<IAiAuditService>();
        var rulesClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var resilientService = new ResilientAiService(
            providers: [corruptProviderMock.Object, rulesClient],
            semanticValidator: validator,
            responseCache: null,
            currentUser: null,
            auditService: auditMock.Object,
            catalogService: catalogMock.Object,
            logger: NullLogger<ResilientAiService>.Instance);

        var context = new AthleteAdaptationContextDto(
            AthleteId: athleteId,
            ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Dolor en tendón rotuliano")],
            CurrentRecords: [],
            Restrictions:
            [
                new ExerciseRestrictionContextDto(
                    BackSquatId,
                    "rodilla",
                    "Reduce estrés en tendón rotuliano",
                    [new AuthorizedSubstituteDto(LegPress45Id, "Prensa de piernas 45°")])
            ]);

        var request = new RoutineSuggestionRequestDto(
            AthleteId: athleteId,
            Goal: "Fuerza Máxima",
            AdaptationContext: context);

        var result = await resilientService.SuggestRoutineAsync(request);

        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.True(result.WasAdapted);
        Assert.NotEmpty(result.Exercises);
        Assert.DoesNotContain(result.Exercises, e => e.ExerciseId == BackSquatId);
        Assert.DoesNotContain(result.Exercises, e => string.Equals(e.ExerciseName, "Sentadilla trasera con barra", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Exercises, e => e.ExerciseId == LegPress45Id && e.ExerciseName == "Prensa de piernas 45°" && !e.IsReference);
        Assert.All(result.Exercises, e =>
        {
            Assert.NotNull(e.ExerciseId);
            Assert.NotEqual(Guid.Empty, e.ExerciseId);
            Assert.False(e.IsReference);
        });
    }

    [Fact]
    public async Task Given_LlmHallucinatesUnrelatedMuscleGroupExercise_When_ValidatingAdaptation_Then_DoesNotSubstituteWithUnrelatedKneeSubstitute()
    {
        var athleteId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);
        var context = CreateKneeInjuryContext(athleteId);

        // El atleta solo tiene restricción de rodilla, pero el LLM alucina un ejercicio de bíceps (otro grupo muscular)
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Tren superior",
            JustificationSummary: "Sesión de empuje y brazos",
            Exercises:
            [
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 75m, BenchPressId),
                new SuggestedExerciseDto("Curl galáctico de bíceps con láser", 3, 12, 0m, Guid.NewGuid())
            ],
            ProviderUsed: "Agente (OpenAI)");

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: context);

        // No debe reemplazar el curl de bíceps alucinado por Prensa de piernas (sin relación)
        Assert.DoesNotContain(result.SanitizedRoutine.Exercises, e => e.ExerciseId == LegPressId);
        Assert.Equal(BenchPressId, result.SanitizedRoutine.Exercises[0].ExerciseId);
        Assert.True(result.SanitizedRoutine.Exercises[1].IsReference);
        Assert.False(result.SanitizedRoutine.WasAdapted);
    }

    [Fact]
    public async Task Given_LlmHallucinatesLegExerciseWhenOnlyAuthorizedSubstituteIsAlreadyInSession_When_ValidatingAdaptation_Then_DoesNotDuplicateExercises()
    {
        var athleteId = Guid.NewGuid();
        var validator = new ExerciseSemanticValidator(CreateCatalogMock().Object);

        var singleSubstituteContext = new AthleteAdaptationContextDto(
            AthleteId: athleteId,
            ActiveInjuries: [CreateActiveInjury(athleteId, "rodilla", "Dolor rotuliano")],
            CurrentRecords: [],
            Restrictions:
            [
                new ExerciseRestrictionContextDto(
                    SquatId,
                    "rodilla",
                    "Reduce estrés rotuliano",
                    [new AuthorizedSubstituteDto(LegPressId, "Prensa de piernas")])
            ]);

        // Prensa de piernas ya está en la sesión y hay un ejercicio alucinado de piernas: no debe duplicar Prensa
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Tren inferior",
            JustificationSummary: "Sesión de piernas",
            Exercises:
            [
                new SuggestedExerciseDto("Prensa de piernas", 4, 8, 70m, LegPressId),
                new SuggestedExerciseDto("Sentadilla espacial con barra", 3, 10, 60m, Guid.NewGuid())
            ],
            ProviderUsed: "Agente (OpenAI)");

        var result = await validator.ValidateDraftAsync(draft, adaptationContext: singleSubstituteContext);

        Assert.Equal(1, result.SanitizedRoutine.Exercises.Count(e => e.ExerciseId == LegPressId));
        Assert.True(result.SanitizedRoutine.Exercises[1].IsReference);
    }
}
