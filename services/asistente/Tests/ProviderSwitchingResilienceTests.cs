using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
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

/// <summary>
/// Pruebas integrales de conmutación de proveedores de IA y respaldo determinístico.
/// Cadena resiliente: Agente OpenAI -> Agente Gemini -> Reglas.
/// Criterios de aceptación:
/// • DADO OpenAI caído CUANDO se pide una sugerencia ENTONCES responde Gemini.
/// • DADO los dos proveedores caídos ENTONCES responden las reglas determinísticas locales.
/// </summary>
public sealed class ProviderSwitchingResilienceTests : IDisposable
{
    private readonly WireMockServer _wireMockServer;
    private readonly Mock<IAiAuditService> _auditServiceMock;
    private readonly Mock<IExerciseLibraryMcpClient> _mcpClientMock;
    private readonly RoutineSuggestionRequestDto _defaultRequest;

    public ProviderSwitchingResilienceTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditServiceMock = new Mock<IAiAuditService>();
        _mcpClientMock = new Mock<IExerciseLibraryMcpClient>();
        _defaultRequest = new RoutineSuggestionRequestDto(
            AthleteId: Guid.Parse("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d"),
            Goal: "Fuerza Máxima",
            MuscleGroupEmphasis: new List<string> { "Pecho", "Tríceps" }
        );
    }

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
    }

    #region Escenario 1: OpenAI caído -> Responde Gemini

    [Fact]
    public async Task Given_OpenAi_Down_Http500_When_SuggestRoutine_Then_Responds_From_Gemini()
    {
        // Arrange: OpenAI falla con HTTP 500
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error in OpenAI"));

        // Gemini responde exitosamente
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - Gemini Fallback")));

        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions", ApiKey = "test-key", TimeoutSeconds = 2 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "Gemini", BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions", ApiKey = "test-gemini-key", TimeoutSeconds = 5 },
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
        Assert.Equal("Fuerza - Gemini Fallback", result.ProposedTitle);
        Assert.NotEmpty(result.Exercises);
        Assert.Equal(_defaultRequest.AthleteId, result.AthleteId);
    }

    [Fact]
    public async Task Given_OpenAi_Connection_Refused_When_SuggestRoutine_Then_Responds_From_Gemini()
    {
        // Arrange: Puerto no existente -> Connection Refused inmediato
        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = "http://127.0.0.1:59999/chat/completions", ApiKey = "test-key", TimeoutSeconds = 1 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - Gemini Recovery")));

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "Gemini", BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions", ApiKey = "test-key", TimeoutSeconds = 5 },
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
        Assert.Equal("Fuerza - Gemini Recovery", result.ProposedTitle);
    }

    #endregion

    #region Escenario 2: Ambos caídos -> Responden las reglas

    [Fact]
    public async Task Given_Both_OpenAi_And_Gemini_Down_Http500_When_SuggestRoutine_Then_Responds_From_RulesEngine()
    {
        // Arrange: Ambos fallan con HTTP 500
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("OpenAI 500"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Gemini 500"));

        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions", ApiKey = "test-key", TimeoutSeconds = 2 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "Gemini", BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions", ApiKey = "test-key", TimeoutSeconds = 2 },
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
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.NotEmpty(result.Exercises);
    }

    [Fact]
    public async Task Given_Both_Down_When_SuggestRoutine_Then_Response_Time_Is_Fast()
    {
        // Arrange
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
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Demoró demasiado: {stopwatch.ElapsedMilliseconds} ms");
    }

    #endregion

    #region Escenario 3: Timeouts

    [Fact]
    public async Task Given_OpenAi_Times_Out_When_SuggestRoutine_Then_Falls_Back_To_Gemini()
    {
        // Arrange: OpenAI tarda 3s pero timeout es 1s
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/openai/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - OpenAI Tardío"))
                    .WithDelay(TimeSpan.FromSeconds(3)));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/gemini/chat/completions"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOpenAiSampleResponse("Fuerza - Gemini Fast")));

        var openAiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "OpenAI", BaseUrl = $"{_wireMockServer.Url}/openai/chat/completions", ApiKey = "test-key", TimeoutSeconds = 1 },
            new HttpClient(),
            _mcpClientMock.Object,
            _auditServiceMock.Object,
            NullLogger<GenericAgentAiClient>.Instance);

        var geminiClient = new GenericAgentAiClient(
            new AgentProviderOptions { Name = "Gemini", BaseUrl = $"{_wireMockServer.Url}/gemini/chat/completions", ApiKey = "test-key", TimeoutSeconds = 5 },
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
        Assert.Equal("Fuerza - Gemini Fast", result.ProposedTitle);
    }

    #endregion

    private static string CreateOpenAiSampleResponse(string title)
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            name = title,
            objective = "Fuerza Máxima",
            justificationSummary = "Propuesta simulada.",
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
                            coachNotes = "Técnica",
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
                                    coachNotes = "Pecho"
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
                prompt_tokens = 50,
                completion_tokens = 100,
                total_tokens = 150
            }
        });
    }
}
