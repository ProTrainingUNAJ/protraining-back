namespace Asistente.Tests;

using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Automated unit and resilience tests for strict JSON Schema validation
/// and AI generative hallucination detection and mitigation (#089 · KAN-384).
/// </summary>
public sealed class JsonSchemaValidationAndHallucinationTests
{
    private readonly Guid _athleteId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
    private readonly Guid _officialBenchPressId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _officialSquatId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly List<CatalogExerciseDto> _catalogDatabase;
    private readonly Mock<IExerciseCatalogService> _catalogServiceMock;
    private readonly ExerciseSemanticValidator _semanticValidator;

    public JsonSchemaValidationAndHallucinationTests()
    {
        _catalogDatabase =
        [
            new(_officialSquatId, "Sentadilla trasera con barra", "Piernas", "Barra olímpica"),
            new(_officialBenchPressId, "Press de banca plano con barra", "Pectorales", "Barra olímpica"),
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Peso muerto convencional", "Espalda", "Barra olímpica"),
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Salto al cajón pliométrico", "Piernas", "Cajón de madera")
        ];

        _catalogServiceMock = new Mock<IExerciseCatalogService>();

        _catalogServiceMock
            .Setup(c => c.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _catalogDatabase.FirstOrDefault(e => e.Id == id));

        _catalogServiceMock
            .Setup(c => c.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
            {
                var norm = name.Trim().ToLowerInvariant();
                return _catalogDatabase.FirstOrDefault(e =>
                    e.Name.ToLowerInvariant().Contains(norm) || norm.Contains(e.Name.ToLowerInvariant()));
            });

        _catalogServiceMock
            .Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_catalogDatabase);

        _semanticValidator = new ExerciseSemanticValidator(_catalogServiceMock.Object);
    }

    #region 1. JSON Schema and Sports Logical Ranges Validation (AiRoutineParser)

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Lo siento, no puedo generar una rutina en este momento.")]
    public void Given_NonJson_Or_Empty_String_When_ParseAndValidate_Then_Returns_Failure_With_Error_Message(string invalidRaw)
    {
        // Act
        var result = AiRoutineParser.ParseAndValidate(invalidRaw);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public void Given_Truncated_Json_Stream_When_ParseAndValidate_Then_Fails_With_Syntax_Error()
    {
        // Arrange (Simulates HTTP connection drop during Ollama streaming)
        const string truncatedJson = """
            {
              "tituloPropuesto": "Fuerza Piernas",
              "resumenJustificacion": "Rutina de sobrecarga",
              "ejercicios": [
                {
                  "ejercicioNombre": "Sentadilla",
                  "series": 4,
            """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(truncatedJson);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.IsSyntaxError);
        Assert.Contains("Sintaxis JSON inválida", result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_Missing_Required_Properties_When_ParseAndValidate_Then_Fails_With_Schema_Validation_Errors()
    {
        // Arrange (Missing tituloPropuesto and ejercicios)
        const string incompleteJson = """
            {
              "resumenJustificacion": "Sin título ni ejercicios"
            }
            """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(incompleteJson);

        // Assert
        Assert.False(result.IsValid);
        Assert.False(result.IsSyntaxError);
        Assert.Contains("tituloPropuesto", result.ErrorMessage);
        Assert.Contains("ejercicios", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0, 10, 70.0, "series")]
    [InlineData(25, 10, 70.0, "series")]
    [InlineData(4, 0, 70.0, "repeticiones")]
    [InlineData(4, 150, 70.0, "repeticiones")]
    [InlineData(4, 10, -5.0, "porcentajeSugerido")]
    [InlineData(4, 10, 140.0, "porcentajeSugerido")]
    public void Given_Sport_Logical_Ranges_Violated_When_ParseAndValidate_Then_Rejects_With_Field_Validation_Error(
        int sets, int reps, decimal pct, string fieldName)
    {
        // Arrange
        var rawJson = $$"""
            {
              "tituloPropuesto": "Rutina con Rango Inválido",
              "resumenJustificacion": "Justificación de prueba",
              "ejercicios": [
                {
                  "ejercicioNombre": "Sentadilla",
                  "series": {{sets}},
                  "repeticiones": {{reps}},
                  "porcentajeSugerido": {{pct}}
                }
              ]
            }
            """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(rawJson);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(fieldName, result.ErrorMessage);
    }

    [Fact]
    public void Given_Json_Wrapped_In_Conversational_Markdown_When_ParseAndValidate_Then_Cleans_Fences_And_Succeeds()
    {
        // Arrange (Conversational LLM response wrapped in markdown block)
        const string conversational = """
            ¡Hola Franco! Claro, aquí tienes la rutina de hipertrofia diseñada para tu atleta:
            ```json
            {
              "tituloPropuesto": "Hipertrofia de Torso",
              "resumenJustificacion": "Énfasis en volumen de pectorales",
              "ejercicios": [
                {
                  "ejercicioNombre": "Press de banca",
                  "series": 4,
                  "repeticiones": 10,
                  "porcentajeSugerido": 75.0
                }
              ]
            }
            ```
            ¡Éxitos en el entrenamiento! Avísame si necesitas modificaciones.
            """;

        // Act
        var result = AiRoutineParser.ParseAndValidate(conversational);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Hipertrofia de Torso", result.ProposedTitle);
        Assert.Single(result.Exercises);
        Assert.Equal("Press de banca", result.Exercises[0].ExerciseName);
    }

    #endregion

    #region 2. Semantic Hallucination Detection and Correction (ExerciseSemanticValidator)

    [Fact]
    public async Task Given_Ai_Invented_Fake_Exercise_Uuid_When_Validating_Draft_Then_Corrects_To_Official_Catalog_Id()
    {
        // Arrange (AI hallucinates fake UUID 99999999-... for a valid catalog exercise name)
        var fakeId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: _athleteId,
            ProposedTitle: "Rutina Fuerza",
            JustificationSummary: "Sobrecarga progresiva",
            Exercises:
            [
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 6, 80m, ExerciseId: fakeId)
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var validation = await _semanticValidator.ValidateDraftAsync(draft);

        // Assert
        Assert.True(validation.IsValid);
        Assert.False(validation.RequiresDeterministicFallback);
        Assert.True(validation.HasCorrections);

        var correctedExercise = validation.SanitizedRoutine.Exercises[0];
        Assert.Equal(_officialBenchPressId, correctedExercise.ExerciseId);
        Assert.Equal(ExerciseValidationStatus.IdCorrected, validation.ExerciseResults[0].Status);
    }

    [Fact]
    public async Task Given_Single_Uncataloged_Exercise_Under_Default_Policy_When_Validating_Draft_Then_Preserves_As_Reference_Without_Failing()
    {
        // Arrange (Single exotic exercise preserved as domain reference under MaxAllowedUncataloged = 1 policy)
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: _athleteId,
            ProposedTitle: "Rutina Experimental",
            JustificationSummary: "Variante no catalogada",
            Exercises:
            [
                new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 8, 75m, _officialSquatId),
                new SuggestedExerciseDto("Salto cuántico con bandas elásticas", 3, 12, 0m, null)
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var validation = await _semanticValidator.ValidateDraftAsync(draft);

        // Assert
        Assert.True(validation.IsValid);
        Assert.False(validation.RequiresDeterministicFallback);

        var uncataloged = validation.SanitizedRoutine.Exercises[1];
        Assert.True(uncataloged.IsReference);
        Assert.Null(uncataloged.ExerciseId);
    }

    [Fact]
    public async Task Given_Multiple_Uncataloged_Exercises_Exceeding_Tolerance_When_Validating_Draft_Then_Requires_Deterministic_Fallback()
    {
        // Arrange (2 uncataloged exercises exceed safety policy threshold of 1)
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: _athleteId,
            ProposedTitle: "Rutina Altamente Alucinada",
            JustificationSummary: "Demasiados inventos de la IA",
            Exercises:
            [
                new SuggestedExerciseDto("Vuelo astral con kettlebell", 3, 10, 0m),
                new SuggestedExerciseDto("Propulsión antigravitatoria", 4, 12, 0m)
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var validation = await _semanticValidator.ValidateDraftAsync(draft);

        // Assert
        Assert.False(validation.IsValid);
        Assert.True(validation.RequiresDeterministicFallback);
        Assert.Contains(validation.ValidationMessages, m => m.Contains("conmutación determinística") || m.Contains("ejercicios no identificados"));
    }

    [Fact]
    public async Task Given_Exercise_Sharing_Words_With_Catalog_Item_When_Biomechanically_Different_Then_Does_Not_Substitute_Erronously()
    {
        // Arrange: "Burpees con salto" shares the word "salto" with "Salto al cajón pliométrico",
        // but must NOT be substituted by it due to strict F1 score threshold (Enzo case).
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: _athleteId,
            ProposedTitle: "Rutina Metabólica",
            JustificationSummary: "Acondicionamiento",
            Exercises:
            [
                new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 8, 75m, _officialSquatId),
                new SuggestedExerciseDto("Burpees con salto", 3, 15, 0m)
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var validation = await _semanticValidator.ValidateDraftAsync(draft);

        // Assert
        Assert.True(validation.IsValid);
        Assert.False(validation.RequiresDeterministicFallback);

        var firstExercise = validation.SanitizedRoutine.Exercises[0];
        Assert.Equal("Sentadilla trasera con barra", firstExercise.ExerciseName);
        Assert.False(firstExercise.IsReference);

        var secondExercise = validation.SanitizedRoutine.Exercises[1];
        Assert.Equal("Burpees con salto", secondExercise.ExerciseName);
        Assert.True(secondExercise.IsReference);
        Assert.Null(secondExercise.ExerciseId);
    }

    #endregion

    #region 3. End-to-End Resilient Fallback Orchestration (ResilientAiService)

    [Fact]
    public async Task Given_Primary_Ai_Provider_Hallucinating_Excessively_When_Suggesting_Routine_Then_Orchestrator_Falls_Back_To_Deterministic_Rules()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Fuerza Máxima",
            MuscleGroupEmphasis: ["Piernas"]
        );

        var corruptProviderMock = new Mock<IAiClient>();
        corruptProviderMock.Setup(p => p.ProviderName).Returns("OllamaAlucinador");
        corruptProviderMock
            .Setup(p => p.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: _athleteId,
                ProposedTitle: "Rutina Rota",
                JustificationSummary: "Alucinaciones masivas",
                Exercises:
                [
                    new SuggestedExerciseDto("Ejercicio Inexistente 1", 3, 10, 0m),
                    new SuggestedExerciseDto("Ejercicio Inexistente 2", 3, 10, 0m),
                    new SuggestedExerciseDto("Ejercicio Inexistente 3", 3, 10, 0m)
                ],
                ProviderUsed: "OllamaAlucinador"
            ));

        var auditMock = new Mock<IAiAuditService>();
        var ruleBasedClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var resilientService = new ResilientAiService(
            providers: [corruptProviderMock.Object, ruleBasedClient],
            semanticValidator: _semanticValidator,
            responseCache: null,
            currentUser: null,
            auditService: auditMock.Object,
            catalogService: _catalogServiceMock.Object,
            logger: NullLogger<ResilientAiService>.Instance
        );

        // Act
        var result = await resilientService.SuggestRoutineAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.NotEmpty(result.Exercises);
        Assert.DoesNotContain(result.Exercises, e => e.ExerciseName.Contains("Inexistente"));
    }

    [Fact]
    public async Task Given_Ai_Adhering_To_Injected_Catalog_Prompt_When_Suggesting_Routine_Then_Approves_Directly_Without_Corrections()
    {
        // Arrange (Happy path optimized in Sprint 2 KAN-382/KAN-383)
        var request = new RoutineSuggestionRequestDto(
            AthleteId: _athleteId,
            Goal: "Fuerza Piernas",
            MuscleGroupEmphasis: ["Piernas"]
        );

        var wellBehavedProviderMock = new Mock<IAiClient>();
        wellBehavedProviderMock.Setup(p => p.ProviderName).Returns("OllamaDisciplinado");
        wellBehavedProviderMock
            .Setup(p => p.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: _athleteId,
                ProposedTitle: "Fuerza Cuádriceps",
                JustificationSummary: "Criterio correcto utilizando catálogo inyectado",
                Exercises:
                [
                    new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 6, 82.5m, _officialSquatId)
                ],
                ProviderUsed: "OllamaDisciplinado"
            ));

        var resilientService = new ResilientAiService(
            providers: [wellBehavedProviderMock.Object],
            semanticValidator: _semanticValidator,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: _catalogServiceMock.Object,
            logger: NullLogger<ResilientAiService>.Instance
        );

        // Act
        var result = await resilientService.SuggestRoutineAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("OllamaDisciplinado", result.ProviderUsed);
        Assert.Single(result.Exercises);
        Assert.Equal(_officialSquatId, result.Exercises[0].ExerciseId);
        Assert.False(result.Exercises[0].IsReference);
    }

    #endregion
}
