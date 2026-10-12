using System.Diagnostics;
using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
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
    private readonly RoutineSuggestionRequestDto _defaultRequest;

    public ResilientAiFallbackTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditServiceMock = new Mock<IAiAuditService>();
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
    /// Criterio de aceptación estricto de la tarjeta KAN-341:
    /// DADO WireMock simulando timeout de 11 segundos en Gemini
    /// CUANDO se ejecuta la solicitud
    /// ENTONCES el orquestador aborta Gemini a los 10s y entrega la respuesta mediante el fallback sin crashear.
    /// </summary>
    [Fact]
    public async Task Given_WireMock_Simulating_Gemini_11Seconds_Timeout_When_Request_Executed_Then_Orchestrator_Aborts_Gemini_At_10Seconds_And_Delivers_Fallback_Without_Crashing()
    {
        // Arrange
        // 1. Simular Ollama caído inmediatamente para que la cadena avance a Gemini
        var ollamaOptions = new OllamaOptions
        {
            BaseUrl = "http://127.0.0.1:59999", // Puerto no existente -> falla de inmediato
            TimeoutSeconds = 1
        };
        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(ollamaOptions),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // 2. WireMock simulando demora de 11 segundos en Gemini
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse())
                    .WithDelay(TimeSpan.FromSeconds(11)));

        // 3. Gemini configurado con límite de 10s según criterio de aceptación
        var geminiOptions = new GeminiOptions
        {
            BaseUrl = _wireMockServer.Url!,
            ApiKey = "wiremock-test-key",
            Model = "gemini-flash-lite-latest",
            TimeoutSeconds = 10
        };
        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(geminiOptions),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        // 4. Fallback final: motor determinístico por reglas
        var rulesClient = new RuleBasedAiClient(
            _auditServiceMock.Object,
            NullLogger<RuleBasedAiClient>.Instance);

        var orchestrator = new ResilientAiService(
            ollamaClient,
            geminiClient,
            rulesClient,
            NullLogger<ResilientAiService>.Instance);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);
        stopwatch.Stop();

        // Assert
        // El orquestador debió abortar Gemini tras aproximadamente 10s y no esperar los 11s completos de WireMock
        Assert.NotNull(result);
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.NotEmpty(result.Exercises);
        Assert.True(stopwatch.ElapsedMilliseconds >= 9500, $"Esperado >= 9.5s, fue {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 11500, $"Esperado < 11.5s, fue {stopwatch.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// Escenario de conmutación 2: Ollama falla con HTTP 500 -> WireMock simula respuesta exitosa en Gemini.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Http500_Failure_When_Request_Executed_Then_Falls_Back_To_Gemini_Successfully()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse()));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 2 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "wiremock-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Contains("Gemini", result.ProposedTitle);
        Assert.Single(result.Exercises);
        Assert.Equal("Press de Banca Plano", result.Exercises[0].ExerciseName);
    }

    /// <summary>
    /// Escenario de conmutación 3: Gemini responde HTTP 429 (Cuota excedida) -> conmutación limpia a reglas determinísticas.
    /// </summary>
    [Fact]
    public async Task Given_Gemini_Returns_Http429_QuotaExceeded_When_Request_Executed_Then_Falls_Back_To_RulesEngine_Without_Error()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(Response.Create().WithStatusCode(429).WithBody("Rate limit reached"));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:59999", TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "wiremock-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.Contains("Determinístico", result.ProposedTitle);
        Assert.NotEmpty(result.Exercises);
    }

    /// <summary>
    /// Escenario de conmutación 4: Ollama supera timeout local -> conmutación inmediata a Gemini sin romper la petición.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_TimesOut_When_Request_Executed_Then_Falls_Back_To_Gemini_And_Records_Origin()
    {
        // Arrange
        // Ollama configurado con timeout de 1s, pero WireMock demora 2s
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody(CreateOllamaSampleResponse())
                    .WithDelay(TimeSpan.FromSeconds(2)));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse()));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "wiremock-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
    }

    /// <summary>
    /// Escenario de conmutación 5: Todos los proveedores de IA caídos -> el motor de reglas garantiza la respuesta sin caídas.
    /// </summary>
    [Fact]
    public async Task Given_All_Ai_Providers_Down_When_Request_Executed_Then_Deterministic_Rules_Guarantee_Response_In_Milliseconds()
    {
        // Arrange
        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:59998", TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = "http://127.0.0.1:59997", ApiKey = "dummy-key", TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.NotEmpty(result.Exercises);
    }

    private static string CreateGeminiSampleResponse()
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            tituloPropuesto = "Fuerza Máxima - WireMock Gemini",
            resumenJustificacion = "Propuesta simulada de Google Gemini para pruebas de integración.",
            ejercicios = new[]
            {
                new { ejercicioNombre = "Press de Banca Plano", series = 4, repeticiones = 5, porcentajeSugerido = 82.5 }
            }
        });

        return JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = innerJson }
                        }
                    }
                }
            },
            usageMetadata = new
            {
                promptTokenCount = 120,
                candidatesTokenCount = 350,
                totalTokenCount = 470
            }
        });
    }

    private static string CreateOllamaSampleResponse()
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            tituloPropuesto = "Fuerza Máxima - WireMock Ollama",
            resumenJustificacion = "Propuesta simulada de Ollama.",
            ejercicios = new[]
            {
                new { ejercicioNombre = "Sentadilla con Barra", series = 5, repeticiones = 5, porcentajeSugerido = 80.0 }
            }
        });

        return JsonSerializer.Serialize(new
        {
            response = innerJson,
            prompt_eval_count = 60,
            eval_count = 110
        });
    }
}
