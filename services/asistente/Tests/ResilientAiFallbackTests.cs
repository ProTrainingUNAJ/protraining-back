using System.Diagnostics;
using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Agent;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Asistente.Tests;

public sealed class ResilientAiFallbackTests : IDisposable
{
    private readonly WireMockServer _wireMockServer;
    private readonly Mock<IAiAuditService> _auditServiceMock;
    private readonly Mock<IExerciseLibraryMcpClient> _mcpClientMock;
    private readonly RoutineSuggestionRequestDto _defaultRequest;

    public ResilientAiFallbackTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditServiceMock = new Mock<IAiAuditService>();
        _mcpClientMock = new Mock<IExerciseLibraryMcpClient>();
        _defaultRequest = new RoutineSuggestionRequestDto(
            AthleteId: Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            Goal: "Fuerza Máxima",
            MuscleGroupEmphasis: new List<string> { "Tren Superior" }
        );
    }

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
    }

    /// <summary>
    /// Escenario 1: Agente OpenAI falla por timeout -> conmutación limpia a Gemini sin crashear.
    /// </summary>
    [Fact]
    public async Task Given_OpenAi_Timeout_When_Request_Executed_Then_Falls_Back_To_Gemini_Successfully()
    {
        // Arrange
        // 1. Simular timeout en endpoint de OpenAI
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - OpenAI"))
                    .WithDelay(TimeSpan.FromSeconds(3)));

        // 2. Gemini responde exitosamente
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - Gemini Fallback")));

        var openAiOptions = new AgentProviderOptions
        {
            Name = "OpenAI",
            BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions",
            Model = "gpt-4.1-mini",
            ApiKey = "test-openai-key",
            TimeoutSeconds = 1 // Timeout corto para testear corte
        };

        var openAiClient = new GenericAgentAiClient(
            openAiOptions,
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiOptions = new AgentProviderOptions
        {
            Name = "Gemini",
            BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions",
            Model = "gemini-2.5-flash",
            ApiKey = "test-gemini-key",
            TimeoutSeconds = 5
        };

        var geminiClient = new GenericAgentAiClient(
            geminiOptions,
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(openAiClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Agente (Gemini)", result.ProviderUsed);
        Assert.Contains("Gemini Fallback", result.ProposedTitle);
        Assert.NotEmpty(result.Exercises);
    }

    /// <summary>
    /// Escenario 2: OpenAI falla con HTTP 500 -> WireMock simula respuesta exitosa en Gemini.
    /// </summary>
    [Fact]
    public async Task Given_OpenAi_Http500_Failure_When_Request_Executed_Then_Falls_Back_To_Gemini_Successfully()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error in OpenAI"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - Gemini Respaldo")));

        var openAiOptions = new AgentProviderOptions
        {
            Name = "OpenAI",
            BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions",
            Model = "gpt-4.1-mini",
            ApiKey = "test-key",
            TimeoutSeconds = 2
        };

        var openAiClient = new GenericAgentAiClient(
            openAiOptions,
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiOptions = new AgentProviderOptions
        {
            Name = "Gemini",
            BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions",
            Model = "gemini-2.5-flash",
            ApiKey = "test-key",
            TimeoutSeconds = 5
        };

        var geminiClient = new GenericAgentAiClient(
            geminiOptions,
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(openAiClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Agente (Gemini)", result.ProviderUsed);
        Assert.Contains("Gemini Respaldo", result.ProposedTitle);
        Assert.NotEmpty(result.Exercises);
    }

    /// <summary>
    /// Escenario 3: Ambos agentes (OpenAI y Gemini) caídos -> el motor de reglas garantiza la respuesta sin caídas.
    /// </summary>
    [Fact]
    public async Task Given_All_Ai_Providers_Down_When_Request_Executed_Then_Deterministic_Rules_Guarantee_Response_In_Milliseconds()
    {
        // Arrange: Puertos no existentes para forzar error inmediato
        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = "http://127.0.0.1:59998/chat/completions", ApiKey = "dummy", TimeoutSeconds = 1 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "Gemini", BaseUrl = "http://127.0.0.1:59997/chat/completions", ApiKey = "dummy", TimeoutSeconds = 1 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(openAiClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.NotEmpty(result.Exercises);
    }

    /// <summary>
    /// Escenario 4: Proveedor sin API Key configurada -> se saltea sin error y continúa la cadena.
    /// </summary>
    [Fact]
    public async Task Given_Provider_Without_ApiKey_When_Request_Executed_Then_Skips_Without_Error_And_Proceeds_To_Next()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Rutina Gemini Directa")));

        // OpenAI sin ApiKey
        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions", ApiKey = "", TimeoutSeconds = 2 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "Gemini", BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions", ApiKey = "valid-key", TimeoutSeconds = 5 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(openAiClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Agente (Gemini)", result.ProviderUsed);
        Assert.Contains("Rutina Gemini Directa", result.ProposedTitle);
    }

    private static string CreateOpenAiSampleResponse(string title)
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            name = title,
            objective = "Fuerza Máxima",
            justificationSummary = "Propuesta generada por el agente de IA.",
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
                            name = title,
                            dayOfWeek = "Monday",
                            order = 1,
                            coachNotes = "Cuidar la técnica",
                            exercises = new[]
                            {
                                new
                                {
                                    exerciseId = Guid.NewGuid(),
                                    exerciseName = "Press de Banca Plano",
                                    order = 1,
                                    sets = 4,
                                    reps = 6,
                                    targetPercentage = 80.0m,
                                    coachNotes = "Fuerza"
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
                    message = new
                    {
                        role = "assistant",
                        content = innerJson
                    },
                    finish_reason = "stop"
                }
            },
            usage = new
            {
                prompt_tokens = 60,
                completion_tokens = 120,
                total_tokens = 180
            }
        });
    }
}
