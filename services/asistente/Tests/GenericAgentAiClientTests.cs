using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Application.DTOs.Ai;
using Application.DTOs.DayAdjustments;
using Application.DTOs.Mcp;
using Application.DTOs.PlanDrafts;
using Application.DTOs.Recommendations;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Agent;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Suite de pruebas unitarias para el agente de IA genérico compatible con OpenAI / Gemini y herramientas MCP.
/// Valida el ciclo de tool calling, límite de 6 llamadas, structured outputs, cero PII y registro en ai_logs.
/// </summary>
public sealed class GenericAgentAiClientTests : IDisposable
{
    private readonly WireMockServer _wireMockServer;
    private readonly Mock<IAiAuditService> _auditMock;
    private readonly Mock<IExerciseLibraryMcpClient> _mcpMock;

    public GenericAgentAiClientTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditMock = new Mock<IAiAuditService>();
        _mcpMock = new Mock<IExerciseLibraryMcpClient>();
    }

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
    }

    /// <summary>
    /// Criterio 1: Con un servidor simulado, el agente llama a BuscarEjercicios antes de armar el borrador,
    /// y el borrador final utiliza únicamente IDs devueltos por la herramienta MCP.
    /// </summary>
    [Fact]
    public async Task Given_SimulatedModel_When_GeneratingPlanDraft_Then_Invokes_SearchExercises_And_Uses_Returned_Ids()
    {
        // Arrange
        var realExerciseId = Guid.NewGuid();
        var toolCallId = "call_mcp_123";

        // Paso 1: El modelo decide llamar a la herramienta BuscarEjercicios
        var turn1Response = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-step1",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = toolCallId,
                                type = "function",
                                function = new
                                {
                                    name = "BuscarEjercicios",
                                    arguments = "{\"muscle\":\"Pecho\",\"limit\":5}"
                                }
                            }
                        }
                    },
                    finish_reason = "tool_calls"
                }
            },
            usage = new { prompt_tokens = 40, completion_tokens = 25, total_tokens = 65 }
        });

        // Paso 2: Tras recibir la respuesta del MCP, el modelo genera el JSON final con el ID real
        var finalDraftJson = JsonSerializer.Serialize(new
        {
            name = "Microciclo Fuerza Pectoral",
            objective = "Hipertrofia y fuerza",
            justificationSummary = "Ejercicios obtenidos de la biblioteca MCP de Entrenamiento.",
            weeks = new[]
            {
                new
                {
                    weekNumber = 1,
                    name = "Semana 1",
                    sessions = new[]
                    {
                        new
                        {
                            name = "Sesión Pecho",
                            dayOfWeek = "Monday",
                            order = 1,
                            coachNotes = "Técnica estricta",
                            exercises = new[]
                            {
                                new
                                {
                                    exerciseId = realExerciseId,
                                    exerciseName = "Press de Banca Plano",
                                    order = 1,
                                    sets = 4,
                                    reps = 8,
                                    targetPercentage = 75.0m,
                                    coachNotes = "Control excéntrico"
                                }
                            }
                        }
                    }
                }
            }
        });

        var turn2Response = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-step2",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = finalDraftJson
                    },
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = 80, completion_tokens = 110, total_tokens = 190 }
        });

        // WireMock configurado para responder turno 1 y luego turno 2
        var scenario = "McpToolCallingScenario";
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .InScenario(scenario)
            .WillSetStateTo("Turn1Done")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(turn1Response));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .InScenario(scenario)
            .WhenStateIs("Turn1Done")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(turn2Response));

        // Simular MCP respondiendo con el ejercicio y su GUID
        var mcpResult = JsonSerializer.Serialize(new
        {
            total = 1,
            items = new[]
            {
                new { id = realExerciseId, name = "Press de Banca Plano", muscleGroup = "Pecho" }
            }
        });

        _mcpMock
            .Setup(m => m.CallToolAsync(
                "BuscarEjercicios",
                It.IsAny<IReadOnlyDictionary<string, object?>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mcpResult);

        var options = new AgentProviderOptions
        {
            Name = "OpenAI",
            BaseUrl = $"{_wireMockServer.Url}/chat/completions",
            Model = "gpt-4.1-mini",
            ApiKey = "test-key",
            TimeoutSeconds = 10
        };

        var client = new GenericAgentAiClient(
            options,
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var request = new CreatePlanDraftRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "Fuerza Pecho",
            Weeks: 1,
            DaysPerWeek: 1,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        var result = await client.TryGeneratePlanDraftAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Microciclo Fuerza Pectoral", result.Name);
        Assert.Single(result.Weeks);
        Assert.Single(result.Weeks[0].Sessions);
        Assert.Single(result.Weeks[0].Sessions[0].Exercises);

        var generatedExercise = result.Weeks[0].Sessions[0].Exercises[0];
        Assert.Equal(realExerciseId, generatedExercise.ExerciseId);
        Assert.Equal("Press de Banca Plano", generatedExercise.ExerciseName);

        // Verificar que el MCP fue llamado con la herramienta BuscarEjercicios
        _mcpMock.Verify(m => m.CallToolAsync(
            "BuscarEjercicios",
            It.Is<IReadOnlyDictionary<string, object?>?>(args => args != null && args.ContainsKey("muscle")),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verificar que ambas llamadas y la tool se auditaron en ai_logs
        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Agente (OpenAI)" &&
            log.ValidationResult == "McpTool:BuscarEjercicios"), It.IsAny<CancellationToken>()), Times.Once);

        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Agente (OpenAI)" &&
            log.ValidationResult.StartsWith("Success") &&
            log.TotalTokens > 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Criterio 2: El mismo cliente genérico funciona con dos configuraciones distintas (OpenAI y Gemini).
    /// </summary>
    [Fact]
    public async Task Given_SameClient_When_Configured_For_OpenAi_And_Gemini_Then_Both_Work()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(CreateSimplePlanResponse("Plan OpenAI")));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(CreateSimplePlanResponse("Plan Gemini")));

        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "OpenAI",
                BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions",
                Model = "gpt-4.1-mini",
                ApiKey = "key-openai"
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "Gemini",
                BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions",
                Model = "gemini-2.5-flash",
                ApiKey = "key-gemini"
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var request = new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            AthleteId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 1,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        var openAiResult = await openAiClient.TryGeneratePlanDraftAsync(request);
        var geminiResult = await geminiClient.TryGeneratePlanDraftAsync(request);

        // Assert
        Assert.NotNull(openAiResult);
        Assert.Equal("Plan OpenAI", openAiResult.Name);
        Assert.Equal("Agente (OpenAI)", openAiClient.ProviderName);

        Assert.NotNull(geminiResult);
        Assert.Equal("Plan Gemini", geminiResult.Name);
        Assert.Equal("Agente (Gemini)", geminiClient.ProviderName);
    }

    /// <summary>
    /// Criterio 3: Límite estricto de máximo 6 llamadas a herramientas MCP por pedido.
    /// Si el modelo pide herramientas repetidamente, se corta a las 6 y se fuerza el cierre.
    /// </summary>
    [Fact]
    public async Task Given_Model_Looping_ToolCalls_When_Executed_Then_Stops_At_Max_6_Calls()
    {
        // Arrange: El modelo siempre pide invocar herramientas
        var loopingToolResponse = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-loop",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = "call_repeat",
                                type = "function",
                                function = new
                                {
                                    name = "BuscarEjercicios",
                                    arguments = "{\"muscle\":\"Piernas\"}"
                                }
                            }
                        }
                    },
                    finish_reason = "tool_calls"
                }
            },
            usage = new { prompt_tokens = 30, completion_tokens = 15, total_tokens = 45 }
        });

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(loopingToolResponse));

        _mcpMock
            .Setup(m => m.CallToolAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"items\":[]}");

        var client = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "OpenAI",
                BaseUrl = $"{_wireMockServer.Url}/chat/completions",
                Model = "gpt-4.1-mini",
                ApiKey = "valid-key"
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance,
            maxToolCalls: 6);

        var request = new CreatePlanDraftRequestDto(
            Goal: "Potencia",
            AthleteId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 1,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        var result = await client.TryGeneratePlanDraftAsync(request);

        // Assert: El MCP no debe haber sido llamado más de 6 veces
        _mcpMock.Verify(m => m.CallToolAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, object?>?>(),
            It.IsAny<CancellationToken>()), Times.AtMost(6));
    }

    /// <summary>
    /// Criterio 4: Proveedor sin API Key configurada se saltea inmediatamente sin error.
    /// </summary>
    [Fact]
    public async Task Given_Provider_Without_ApiKey_When_GeneratePlanDraft_Then_Returns_Null_Without_Exceptions()
    {
        // Arrange
        var client = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "OpenAI",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4.1-mini",
                ApiKey = "" // Sin clave
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var request = new CreatePlanDraftRequestDto(
            Goal: "Fuerza",
            AthleteId: Guid.NewGuid(),
            Weeks: 1,
            DaysPerWeek: 1,
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        var result = await client.TryGeneratePlanDraftAsync(request);

        // Assert
        Assert.Null(result);
        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Agente (OpenAI)" &&
            log.ValidationResult == "SinApiKey"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Criterio 5: Cero PII. La información enviada al modelo no contiene nombres ni emails.
    /// </summary>
    [Fact]
    public void Given_PlanDraftRequest_When_BuildingPrompt_Then_Contains_No_PII()
    {
        // Arrange
        var request = new CreatePlanDraftRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "Fuerza Tren Inferior",
            Weeks: 4,
            DaysPerWeek: 3,
            StartDate: new DateOnly(2026, 11, 20));

        // Act
        var prompt = AgentPromptBuilder.BuildPlanDraftUserPrompt(request);

        // Assert: No contiene emails, arrobas ni nombres
        Assert.DoesNotContain("@", prompt);
        Assert.DoesNotContain("email", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nombre", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fuerza Tren Inferior", prompt);
        Assert.Contains("4 semanas", prompt);
        Assert.Contains("3 días por semana", prompt);
    }

    /// <summary>
    /// Criterio 6: Redacción de justificación del ajuste del día por el agente de IA.
    /// </summary>
    [Fact]
    public async Task Given_DayAdjustmentItems_When_TryGenerateJustification_Then_Generates_Text_And_Audits()
    {
        // Arrange
        var justificationText = "Por molestia reportada en rodilla, se sustituyó la sentadilla por prensa 45° reduciendo el estrés patelofemoral.";
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-just",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = justificationText },
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = 50, completion_tokens = 30, total_tokens = 80 }
        });

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(responseJson));

        var client = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "OpenAI",
                BaseUrl = $"{_wireMockServer.Url}/chat/completions",
                Model = "gpt-4.1-mini",
                ApiKey = "valid-key"
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var items = new[]
        {
            new DayAdjustmentItemDto(
                OriginalExerciseId: Guid.NewGuid(),
                OriginalExerciseName: "Sentadilla trasera",
                SuggestedSubstituteExerciseId: Guid.NewGuid(),
                SuggestedSubstituteExerciseName: "Prensa 45°",
                SubstituteExerciseId: Guid.NewGuid(),
                SubstituteExerciseName: "Prensa 45°",
                Zone: "Rodilla",
                PainLevel: 4,
                Reason: "Dolor en tendón rotuliano",
                AvailableSubstitutes: [])
        };

        // Act
        var result = await client.TryGenerateDayAdjustmentJustificationAsync(items);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(justificationText, result);

        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Agente (OpenAI)" &&
            log.ValidationResult == "SuccessDayAdjustment" &&
            log.PromptTokens == 50 &&
            log.CompletionTokens == 30), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ValidTrainingRecommendationFromAgent_When_Generated_Then_ValidatesAndAudits()
    {
        // Arrange
        var innerJson = JsonSerializer.Serialize(new
        {
            title = "Refuerzo de salto vertical",
            reason = "salto por debajo del promedio",
            recommendationText = "Se detectó salto por debajo del promedio (34 cm vs 42 cm). Sumá dos bloques semanales de pliometría progresiva."
        });

        var completionJson = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-rec-train",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = innerJson },
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = 90, completion_tokens = 40, total_tokens = 130 }
        });

        _wireMockServer
            .Given(Request.Create().WithPath("/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(completionJson));

        var client = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "OpenAI",
                BaseUrl = $"{_wireMockServer.Url}/v1",
                Model = "gpt-4.1-mini",
                ApiKey = "test-key",
                TimeoutSeconds = 10
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var request = new TrainingRecommendationRequestDto(
            Reason: "salto por debajo del promedio",
            Metrics: [new ProfileMetricComparisonDto("Salto Vertical CMJ (cm)", 34, 42, "cm", -19.0, "Por debajo del promedio (-19.0%)")],
            CategoryName: "Primera División",
            SuggestedFocus: "Incorporar pliometría de bajo impacto",
            AthleteId: Guid.NewGuid());

        // Act
        var result = await client.TryGenerateTrainingRecommendationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Agente (OpenAI)", result!.ProviderUsed);
        Assert.Equal("salto por debajo del promedio", result.Reason);
        Assert.Contains("34 cm vs 42 cm", result.RecommendationText);

        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Agente (OpenAI)" &&
            log.IsValid &&
            log.ValidationResult == "Valido" &&
            log.PromptTokens == 90 &&
            log.CompletionTokens == 40), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_TrainingRecommendationWithSupplement_When_AgentResponds_Then_RejectsAndFallsBackToRules()
    {
        // Arrange
        var innerJson = JsonSerializer.Serialize(new
        {
            title = "Potencia y suplementación",
            reason = "salto por debajo del promedio",
            recommendationText = "Agregá creatina y proteína en polvo después de entrenar."
        });

        var completionJson = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-rec-bad",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = innerJson },
                    finish_reason = "stop"
                }
            }
        });

        _wireMockServer
            .Given(Request.Create().WithPath("/v1/chat/completions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(completionJson));

        var agentClient = new GenericAgentAiClient(
            new AgentProviderOptions
            {
                Name = "OpenAI",
                BaseUrl = $"{_wireMockServer.Url}/v1",
                Model = "gpt-4.1-mini",
                ApiKey = "test-key",
                TimeoutSeconds = 10
            },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var resilient = new ResilientAiService(
            [agentClient, rulesClient],
            semanticValidator: null,
            responseCache: null,
            NullLogger<ResilientAiService>.Instance);

        var request = new TrainingRecommendationRequestDto(
            Reason: "salto por debajo del promedio",
            Metrics: [new ProfileMetricComparisonDto("Salto Vertical CMJ (cm)", 34, 42, "cm", -19.0)]);

        // Act
        var result = await resilient.GenerateTrainingRecommendationAsync(request);

        // Assert
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.Equal("salto por debajo del promedio", result.Reason);
        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Agente (OpenAI)" &&
            !log.IsValid &&
            log.ValidationResult == "SuplementoDetectado"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_NutritionRecommendationFromAgent_ValidAndInvalid_Then_EnforcesApprovedFoodsAndDisclaimer()
    {
        // Arrange: primero devuelve un alimento no aprobado ("pizza"), luego una válida en segundo test o con ResilientAiService
        var badInnerJson = JsonSerializer.Serialize(new
        {
            suggestedPlateComposition = "Comé pizza con pollo para recuperar energía.",
            hydrationAndSleepGuidelines = "Tomá 2.5 litros de agua y dormí 8 horas.",
            mentionedFoods = new[] { "pollo" },
            legalDisclaimer = "Esto es una recomendación general; para un plan a tu medida, consultá a un nutricionista."
        });

        var goodInnerJson = JsonSerializer.Serialize(new
        {
            suggestedPlateComposition = "Armá tu plato con pollo, arroz y zanahoria para cubrir entre 100 g y 140 g de proteína.",
            hydrationAndSleepGuidelines = "Tomá 2.5 litros de agua y descansá 8 horas.",
            mentionedFoods = new[] { "pollo", "arroz", "zanahoria" },
            legalDisclaimer = "Esto es una recomendación general; para un plan a tu medida, consultá a un nutricionista."
        });

        _wireMockServer
            .Given(Request.Create().WithPath("/v1/chat/completions").UsingPost().WithHeader("Authorization", "Bearer bad-key"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new
            {
                id = "chatcmpl-nut-bad",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = badInnerJson }, finish_reason = "stop" } }
            })));

        _wireMockServer
            .Given(Request.Create().WithPath("/v1/chat/completions").UsingPost().WithHeader("Authorization", "Bearer good-key"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(JsonSerializer.Serialize(new
            {
                id = "chatcmpl-nut-good",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = goodInnerJson }, finish_reason = "stop" } }
            })));

        var goodClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = $"{_wireMockServer.Url}/v1", Model = "gpt-4.1-mini", ApiKey = "good-key", TimeoutSeconds = 10 },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var badClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = $"{_wireMockServer.Url}/v1", Model = "gpt-4.1-mini", ApiKey = "bad-key", TimeoutSeconds = 10 },
            new HttpClient(),
            _mcpMock.Object,
            _auditMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var request = new NutritionRecommendationRequestDto(
            AthleticGoal: "Ganar masa muscular",
            ApprovedFoods: ["pollo", "arroz", "zanahoria"],
            MinProteinGrams: 100,
            MaxProteinGrams: 140,
            HydrationLiters: 2.5,
            SleepHours: 8);

        // Act & Assert - caso válido
        var validResult = await goodClient.TryGenerateNutritionRecommendationAsync(request);
        Assert.NotNull(validResult);
        Assert.Equal("Agente (OpenAI)", validResult!.ProviderUsed);
        Assert.Equal("Esto es una recomendación general; para un plan a tu medida, consultá a un nutricionista.", validResult.LegalDisclaimer);

        // Act & Assert - caso inválido cae a reglas via ResilientAiService
        var rulesClient = new RuleBasedAiClient(_auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var resilient = new ResilientAiService(
            [badClient, rulesClient],
            semanticValidator: null,
            responseCache: null,
            currentUser: null,
            auditService: null,
            catalogService: null,
            quotaService: null,
            logger: NullLogger<ResilientAiService>.Instance,
            identityHabitsClient: HabitsRecommendationsClientFactory.Enabled());

        var fallbackResult = await resilient.GenerateNutritionRecommendationAsync(request);
        Assert.Equal("Reglas", fallbackResult.ProviderUsed);
        _auditMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            !log.IsValid &&
            log.ValidationResult == "AlimentoNoAprobado"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string CreateSimplePlanResponse(string planName)
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            name = planName,
            objective = "Objetivo deportivo",
            justificationSummary = "Resumen técnico de la planificación.",
            weeks = new[]
            {
                new
                {
                    weekNumber = 1,
                    name = "Semana 1",
                    sessions = new[]
                    {
                        new
                        {
                            name = "Sesión 1",
                            dayOfWeek = "Monday",
                            order = 1,
                            coachNotes = "Notas",
                            exercises = new[]
                            {
                                new
                                {
                                    exerciseId = Guid.NewGuid(),
                                    exerciseName = "Sentadilla",
                                    order = 1,
                                    sets = 4,
                                    reps = 6,
                                    targetPercentage = 80.0m
                                }
                            }
                        }
                    }
                }
            }
        });

        return JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = innerJson },
                    finish_reason = "stop"
                }
            },
            usage = new { prompt_tokens = 50, completion_tokens = 50, total_tokens = 100 }
        });
    }
}
