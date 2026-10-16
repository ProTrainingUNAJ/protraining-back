namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Infrastructure.Services.Ai.Rules;
using Infrastructure.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Pruebas unitarias para el validador semántico de ejercicios (KAN-344):
/// Verifica la existencia de ejercicios e identificadores contra el catálogo oficial,
/// la corrección automática de identificadores inexistentes, la inclusión de ejercicios
/// de referencia (Ajuste N2) y la conmutación a generación determinística segura.
/// </summary>
public sealed class ExerciseSemanticValidatorTests
{
    private readonly ExerciseCatalogService _catalogService;
    private readonly ExerciseSemanticValidator _validator;

    public ExerciseSemanticValidatorTests()
    {
        _catalogService = new ExerciseCatalogService(NullLogger<ExerciseCatalogService>.Instance);
        _validator = new ExerciseSemanticValidator(_catalogService);
    }

    [Fact]
    public async Task Given_Draft_With_NonCataloged_ExerciseId_When_Exercise_Name_Exists_In_Catalog_Then_Corrects_To_Official_Catalog_Id()
    {
        // Arrange: El ejercicio posee un ejercicioId no registrado en la base de datos pero el nombre corresponde a un ejercicio oficial
        var uncatalogedId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Fuerza de Piernas",
            JustificationSummary: "Planificación con enfoque en sobrecarga progresiva",
            Exercises:
            [
                new SuggestedExerciseDto(
                    ExerciseName: "Sentadilla trasera con barra",
                    Sets: 4,
                    Reps: 6,
                    SuggestedPercentage: 80.0m,
                    ExerciseId: uncatalogedId,
                    IsReference: false
                )
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var result = await _validator.ValidateDraftAsync(draft);

        // Assert: El validador detecta la discordancia y corrige el ejercicioId al ID oficial del catálogo
        Assert.True(result.IsValid);
        Assert.False(result.RequiresDeterministicFallback);
        Assert.True(result.HasCorrections);
        Assert.Equal(1, result.CorrectedCount);

        var validatedItem = Assert.Single(result.ExerciseResults);
        Assert.Equal(ExerciseValidationStatus.IdCorrected, validatedItem.Status);
        Assert.Contains(uncatalogedId.ToString(), validatedItem.Message);

        var sanitizedExercise = Assert.Single(result.SanitizedRoutine.Exercises);
        Assert.NotNull(sanitizedExercise.ExerciseId);
        Assert.NotEqual(uncatalogedId, sanitizedExercise.ExerciseId);

        var expectedRealCatalogId = ExerciseCatalogService.GenerateDeterministicGuid("Sentadilla trasera con barra");
        Assert.Equal(expectedRealCatalogId, sanitizedExercise.ExerciseId);
        Assert.False(sanitizedExercise.IsReference);
    }

    [Fact]
    public async Task Given_Draft_Without_ExerciseId_When_Exercise_Name_Exists_In_Catalog_Then_Resolves_And_Binds_Official_Catalog_Id()
    {
        // Arrange: Ejercicio perteneciente al catálogo recibido con ejercicioId nulo
        var athleteId = Guid.NewGuid();
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Pectorales y Espalda",
            JustificationSummary: "Sesión enfocada en tren superior y empujes",
            Exercises:
            [
                new SuggestedExerciseDto(
                    ExerciseName: "Press de banca plano con barra",
                    Sets: 4,
                    Reps: 8,
                    SuggestedPercentage: 75.0m,
                    ExerciseId: null,
                    IsReference: false
                )
            ],
            ProviderUsed: "Gemini"
        );

        // Act
        var result = await _validator.ValidateDraftAsync(draft);

        // Assert: Se enlaza automáticamente el ejercicioId oficial manteniendo el ejercicio como válido
        Assert.True(result.IsValid);
        Assert.True(result.HasCorrections);
        Assert.Equal(1, result.CorrectedCount);

        var sanitizedExercise = Assert.Single(result.SanitizedRoutine.Exercises);
        var expectedRealCatalogId = ExerciseCatalogService.GenerateDeterministicGuid("Press de banca plano con barra");
        Assert.Equal(expectedRealCatalogId, sanitizedExercise.ExerciseId);
        Assert.False(sanitizedExercise.IsReference);
    }

    [Fact]
    public async Task Given_Draft_With_Valid_ExerciseId_When_Matches_Catalog_Then_Preserves_Id_And_Marks_Valid()
    {
        // Arrange: El ejercicio ya cuenta con el identificador exacto registrado en el catálogo
        var athleteId = Guid.NewGuid();
        var legitimateId = ExerciseCatalogService.GenerateDeterministicGuid("Peso muerto convencional");

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Fuerza Máxima",
            JustificationSummary: "Trabajo multiarticular con cargas submáximas",
            Exercises:
            [
                new SuggestedExerciseDto(
                    ExerciseName: "Peso muerto convencional",
                    Sets: 3,
                    Reps: 5,
                    SuggestedPercentage: 85.0m,
                    ExerciseId: legitimateId,
                    IsReference: false
                )
            ],
            ProviderUsed: "Gemini"
        );

        // Act
        var result = await _validator.ValidateDraftAsync(draft);

        // Assert: Se preserva la información original sin requerir modificaciones
        Assert.True(result.IsValid);
        Assert.False(result.HasCorrections);
        Assert.Equal(1, result.ValidCount);

        var validatedItem = Assert.Single(result.ExerciseResults);
        Assert.Equal(ExerciseValidationStatus.Valid, validatedItem.Status);

        var sanitizedExercise = Assert.Single(result.SanitizedRoutine.Exercises);
        Assert.Equal(legitimateId, sanitizedExercise.ExerciseId);
        Assert.False(sanitizedExercise.IsReference);
    }

    [Fact]
    public async Task Given_Draft_With_Uncataloged_Exercise_When_Inspecting_Under_Adjustment_Rule_Then_Brings_As_Reference_With_Null_Id()
    {
        // Arrange (Ajuste N2): Propuesta que combina un ejercicio oficial con una variante no registrada en el catálogo
        var athleteId = Guid.NewGuid();
        var uncatalogedId = Guid.NewGuid();

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Rutina con Variación Específica",
            JustificationSummary: "Sesión con trabajo técnico y variantes no estándar",
            Exercises:
            [
                new SuggestedExerciseDto(
                    ExerciseName: "Sentadilla trasera con barra",
                    Sets: 4,
                    Reps: 6,
                    SuggestedPercentage: 80.0m,
                    ExerciseId: null
                ),
                new SuggestedExerciseDto(
                    ExerciseName: "Sentadilla cosaca con mancuerna hexagonal",
                    Sets: 3,
                    Reps: 12,
                    SuggestedPercentage: 50.0m,
                    ExerciseId: uncatalogedId,
                    IsReference: false
                )
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var result = await _validator.ValidateDraftAsync(draft);

        // Assert: El ejercicio oficial se asocia al catálogo y la variante se preserva como referencia para el preparador físico
        Assert.True(result.IsValid);
        Assert.False(result.RequiresDeterministicFallback);
        Assert.Equal(1, result.CorrectedCount);
        Assert.Equal(1, result.ReferenceCount);

        var refExercise = result.SanitizedRoutine.Exercises.First(e => e.ExerciseName.Contains("cosaca"));
        Assert.Null(refExercise.ExerciseId);
        Assert.True(refExercise.IsReference);
    }

    [Fact]
    public async Task Given_Draft_Where_All_Exercises_Are_Uncataloged_When_Inspecting_Draft_Then_Requires_Deterministic_Fallback()
    {
        // Arrange: Propuesta con ejercicios e identificadores no registrados en el catálogo
        var athleteId = Guid.NewGuid();

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Rutina No Registrada",
            JustificationSummary: "Propuesta con movimientos no existentes en la biblioteca oficial",
            Exercises:
            [
                new SuggestedExerciseDto("Pase de granjero unilateral con kettlebell", 3, 10, 70m, Guid.NewGuid()),
                new SuggestedExerciseDto("Lanzamiento rotacional con balón medicinal", 3, 10, 70m, Guid.NewGuid())
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var result = await _validator.ValidateDraftAsync(draft);

        // Assert: Al no contar con ejercicios catalogados que anclen la rutina, se requiere conmutación a determinístico
        Assert.False(result.IsValid);
        Assert.True(result.RequiresDeterministicFallback);
        Assert.Equal(2, result.ReferenceCount);
        Assert.Contains("Ningún ejercicio propuesto existe en el catálogo", string.Join(" ", result.ValidationMessages));
    }

    [Fact]
    public async Task Given_Draft_With_Uncataloged_Exercises_When_ValidateOrFallback_Executed_Then_Conmutes_To_Safe_Deterministic_Generation()
    {
        // Arrange: Solicitud inicial y propuesta recibida no reconciliable con el catálogo
        var athleteId = Guid.NewGuid();
        var request = new RoutineSuggestionRequestDto(
            AthleteId: athleteId,
            Goal: "Fuerza Máxima de Piernas",
            MuscleGroupEmphasis: ["Piernas"]
        );

        var uncatalogedDraft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Propuesta No Catalogada",
            JustificationSummary: "Ejercicios no coincidentes con el catálogo",
            Exercises:
            [
                new SuggestedExerciseDto("Remo invertido en anillas con sobrecarga", 3, 10, 70m, Guid.NewGuid())
            ],
            ProviderUsed: "Ollama"
        );

        // Act: Ejecución del validador con conmutación automática de respaldo
        var finalProposal = await _validator.ValidateOrFallbackAsync(uncatalogedDraft, request);

        // Assert: Conmuta de forma segura al motor determinístico con ejercicios e IDs reales
        Assert.Equal(athleteId, finalProposal.AthleteId);
        Assert.Contains("Determinístico Seguro", finalProposal.ProposedTitle);
        Assert.Contains("Reglas", finalProposal.ProviderUsed);
        Assert.NotEmpty(finalProposal.Exercises);

        foreach (var exercise in finalProposal.Exercises)
        {
            Assert.NotNull(exercise.ExerciseId);
            Assert.NotEqual(Guid.Empty, exercise.ExerciseId);
            Assert.False(exercise.IsReference);
            var exists = await _catalogService.FindByIdAsync(exercise.ExerciseId.Value);
            Assert.NotNull(exists);
        }
    }

    [Fact]
    public async Task Given_Draft_With_Uncataloged_Exercise_Under_Strict_Policy_When_Inspecting_Draft_Then_Requires_Fallback()
    {
        // Arrange: Configuración de política estricta que exige coincidencia total en catálogo
        var athleteId = Guid.NewGuid();
        var draft = new RoutineSuggestionResponseDto(
            AthleteId: athleteId,
            ProposedTitle: "Rutina Mixta",
            JustificationSummary: "Combinación de ejercicios",
            Exercises:
            [
                new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 6, 80m),
                new SuggestedExerciseDto("Curl femoral con banda elástica", 3, 10, 60m, Guid.NewGuid())
            ],
            ProviderUsed: "Ollama"
        );

        // Act
        var result = await _validator.ValidateDraftAsync(draft, SemanticValidationPolicy.Strict);

        // Assert: Bajo política estricta no se admiten referencias y se exige conmutación
        Assert.False(result.IsValid);
        Assert.True(result.RequiresDeterministicFallback);
        Assert.Equal(1, result.UncatalogedCount);
    }

    [Fact]
    public async Task Given_Raw_Json_From_External_Model_When_Parsed_And_Validated_Then_Produces_Clean_Sanitized_Proposal()
    {
        // Arrange: Payload JSON representativo recibido desde un proveedor externo
        const string rawAiJson = """
        {
          "tituloPropuesto": "Fuerza Máxima de Piernas",
          "resumenJustificacion": "Énfasis en sentadillas y potencia.",
          "ejercicios": [
            {
              "ejercicioId": "11111111-2222-3333-4444-555555555555",
              "ejercicioNombre": "Sentadilla trasera con barra (Back Squat)",
              "series": 4,
              "repeticiones": 5,
              "porcentajeSugerido": 82.5
            },
            {
              "ejercicioNombre": "Prensa de piernas 45°",
              "series": 3,
              "repeticiones": 8,
              "porcentajeSugerido": 72.5
            }
          ]
        }
        """;

        // Act 1: Parseo de sintaxis y esquema
        var parseResult = AiRoutineParser.ParseAndValidate(rawAiJson);
        Assert.True(parseResult.IsValid);

        var draft = new RoutineSuggestionResponseDto(
            AthleteId: Guid.NewGuid(),
            ProposedTitle: parseResult.ProposedTitle!,
            JustificationSummary: parseResult.JustificationSummary!,
            Exercises: parseResult.Exercises.ToList(),
            ProviderUsed: "Ollama"
        );

        // Act 2: Validación semántica y resolución contra el catálogo
        var validation = await _validator.ValidateDraftAsync(draft);

        // Assert: Ambos ejercicios quedan vinculados a los identificadores oficiales del catálogo
        Assert.True(validation.IsValid);
        Assert.False(validation.RequiresDeterministicFallback);
        Assert.Equal(2, validation.CorrectedCount);

        var sentadilla = validation.SanitizedRoutine.Exercises[0];
        Assert.NotEqual(Guid.Parse("11111111-2222-3333-4444-555555555555"), sentadilla.ExerciseId);
        Assert.NotNull(sentadilla.ExerciseId);
        Assert.False(sentadilla.IsReference);

        var prensa = validation.SanitizedRoutine.Exercises[1];
        Assert.NotNull(prensa.ExerciseId);
        Assert.False(prensa.IsReference);
    }

    [Fact]
    public void Given_Deterministic_Rule_Engine_When_Generating_Proposal_Then_All_Exercises_Contain_Valid_Deterministic_Catalog_Ids()
    {
        // Act: Generación mediante el motor de reglas determinísticas
        var (title, justification, exercises) = DeterministicRuleEngine.GenerateProposal(
            "Fuerza Máxima",
            ["Piernas"]);

        // Assert: Todos los ejercicios generados cuentan con identificadores reproducibles del catálogo
        Assert.NotEmpty(title);
        Assert.NotEmpty(justification);
        Assert.NotEmpty(exercises);

        foreach (var exercise in exercises)
        {
            Assert.NotNull(exercise.ExerciseId);
            Assert.NotEqual(Guid.Empty, exercise.ExerciseId);
            Assert.False(exercise.IsReference);
        }
    }

    [Fact]
    public async Task Given_ResilientAiService_With_Validator_When_Model_Returns_NonCataloged_Id_Then_Corrects_Before_Returning()
    {
        // Arrange: Integración con la cadena de resiliencia
        var athleteId = Guid.NewGuid();
        var nonCatalogedId = Guid.NewGuid();
        var request = new RoutineSuggestionRequestDto(athleteId, "Fuerza Máxima de Piernas", ["Piernas"]);

        var fakeAiResponseJson = $$"""
        {
          "response": "{\"tituloPropuesto\": \"Fuerza Piernas\", \"resumenJustificacion\": \"Sobrecarga progresiva\", \"ejercicios\": [{\"ejercicioId\": \"{{nonCatalogedId}}\", \"ejercicioNombre\": \"Sentadilla trasera con barra\", \"series\": 4, \"repeticiones\": 6, \"porcentajeSugerido\": 80.0}]}"
        }
        """;

        var handler = new TestHttpMessageHandler(fakeAiResponseJson);
        var httpClient = new HttpClient(handler);
        var ollamaOptions = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Services.Ai.Ollama.OllamaOptions { BaseUrl = "http://localhost:11434", Model = "llama3.2:3b", TimeoutSeconds = 10 });
        var auditService = new Moq.Mock<Application.Interfaces.Services.IAiAuditService>().Object;

        var ollamaClient = new Infrastructure.Services.Ai.Ollama.OllamaClient(httpClient, ollamaOptions, auditService, NullLogger<Infrastructure.Services.Ai.Ollama.OllamaClient>.Instance);
        var geminiOptions = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Services.Ai.Gemini.GeminiOptions { ApiKey = "", Model = "gemini-1.5-flash" });
        var geminiClient = new Infrastructure.Services.Ai.Gemini.GeminiClient(new HttpClient(), geminiOptions, auditService, NullLogger<Infrastructure.Services.Ai.Gemini.GeminiClient>.Instance);
        var rulesClient = new RuleBasedAiClient(auditService, NullLogger<RuleBasedAiClient>.Instance);

        var resilientService = new Infrastructure.Services.Ai.ResilientAiService(
            ollamaClient,
            geminiClient,
            rulesClient,
            _validator,
            NullLogger<Infrastructure.Services.Ai.ResilientAiService>.Instance
        );

        // Act
        var result = await resilientService.SuggestRoutineAsync(request);

        // Assert: ResilientAiService retorna la rutina con el ejercicioId corregido al correspondiente del catálogo
        Assert.Equal("Ollama", result.ProviderUsed);
        var exercise = Assert.Single(result.Exercises);
        Assert.NotNull(exercise.ExerciseId);
        Assert.NotEqual(nonCatalogedId, exercise.ExerciseId);
        var realId = ExerciseCatalogService.GenerateDeterministicGuid("Sentadilla trasera con barra");
        Assert.Equal(realId, exercise.ExerciseId);
    }

    private sealed class TestHttpMessageHandler(string responseContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
