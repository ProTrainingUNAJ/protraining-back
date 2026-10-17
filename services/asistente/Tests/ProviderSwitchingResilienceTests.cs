using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
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
/// Pruebas integrales de conmutación de proveedores de IA y respaldo determinístico (#080 / KAN-375).
/// Criterios de aceptación:
/// • DADO Ollama caído CUANDO se pide algo ENTONCES responde Gemini.
/// • DADO los dos caídos ENTONCES responden las reglas.
/// Simula caídas de red, códigos de error HTTP (500, 429), timeouts y formatos inválidos.
/// </summary>
public sealed class ProviderSwitchingResilienceTests : IDisposable
{
    private readonly WireMockServer _wireMockServer;
    private readonly Mock<IAiAuditService> _auditServiceMock;
    private readonly RoutineSuggestionRequestDto _defaultRequest;

    public ProviderSwitchingResilienceTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditServiceMock = new Mock<IAiAuditService>();
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

    #region Escenario 1: Ollama caído -> Responde Gemini

    /// <summary>
    /// Criterio de aceptación 1: DADO Ollama caído con HTTP 500 CUANDO se pide algo ENTONCES responde Gemini.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Down_Http500_When_SuggestRoutine_Then_Responds_From_Gemini()
    {
        // Arrange
        // Ollama devuelve error 500
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error in Ollama"));

        // Gemini responde exitosamente con rutina válida
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse("Fuerza - Gemini Fallback")));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 2 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "test-gemini-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Equal("Fuerza - Gemini Fallback", result.ProposedTitle);
        Assert.NotEmpty(result.Exercises);
        Assert.Equal(_defaultRequest.AthleteId, result.AthleteId);

        // Verifica que Ollama fue auditado con error HTTP
        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "ErrorHttp"), It.IsAny<CancellationToken>()), Times.Once);

        // Verifica que Gemini fue auditado como exitoso
        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" &&
            log.IsValid &&
            log.ValidationResult == "Valido"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Criterio de aceptación 1 (Variante Conexión): DADO Ollama caído por conexión rechazada (puerto cerrado)
    /// CUANDO se pide algo ENTONCES conmuta limpiamente y responde Gemini.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Down_ConnectionRefused_When_SuggestRoutine_Then_Responds_From_Gemini()
    {
        // Arrange
        // Gemini responde 200
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse("Fuerza - Gemini Recuperación Red")));

        // Ollama configurado hacia un puerto inexistente
        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:59998", TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "test-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Equal("Fuerza - Gemini Recuperación Red", result.ProposedTitle);
        Assert.NotEmpty(result.Exercises);
    }

    /// <summary>
    /// Criterio de aceptación 1 (Variante Timeout): DADO Ollama bloqueado por timeout
    /// CUANDO se pide algo ENTONCES aborta Ollama y responde Gemini sin degradar a reglas.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Down_Timeout_When_SuggestRoutine_Then_Responds_From_Gemini()
    {
        // Arrange
        // Ollama con retardo de 2s mientras su timeout es de 1s
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody(CreateOllamaSampleResponse("Rutina Lenta"))
                    .WithDelay(TimeSpan.FromSeconds(2)));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse("Fuerza - Gemini por Timeout")));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "test-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Equal("Fuerza - Gemini por Timeout", result.ProposedTitle);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "Timeout"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Criterio de aceptación 1 (Variante Formato Inválido): DADO Ollama respondiendo 200 pero con JSON sintácticamente corrupto
    /// CUANDO se pide algo ENTONCES la descarta, audita como JsonInvalido y conmuta a Gemini.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Returns_Corrupted_Json_When_SuggestRoutine_Then_Responds_From_Gemini()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(new
                    {
                        response = "{ no es un json valido",
                        prompt_eval_count = 10,
                        eval_count = 20
                    })));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse("Fuerza - Gemini tras JsonInvalido")));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 2 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "test-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Equal("Fuerza - Gemini tras JsonInvalido", result.ProposedTitle);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "JsonInvalido"), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Escenario 2: Ambos caídos (Ollama y Gemini) -> Responden las reglas

    /// <summary>
    /// Criterio de aceptación 2: DADO Ollama y Gemini caídos con HTTP 500
    /// CUANDO se pide algo ENTONCES responden las reglas determinísticas.
    /// </summary>
    [Fact]
    public async Task Given_Both_Ollama_And_Gemini_Down_Http500_When_SuggestRoutine_Then_Responds_From_DeterministicRules()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Ollama 500"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Gemini 500"));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 2 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "wiremock-key", TimeoutSeconds = 2 }),
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
        Assert.Equal(_defaultRequest.AthleteId, result.AthleteId);

        // Verifica auditoría de ambos proveedores caídos
        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" && !log.IsValid && log.ValidationResult == "ErrorHttp"), It.IsAny<CancellationToken>()), Times.Once);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" && !log.IsValid && log.ValidationResult == "ErrorHttp"), It.IsAny<CancellationToken>()), Times.Once);

        // Verifica auditoría del motor determinístico exitoso
        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Reglas" && log.IsValid && log.ValidationResult == "Valido"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Criterio de aceptación 2 (Variante Conexión): DADO Ollama y Gemini inalcanzables en la red (puertos cerrados)
    /// CUANDO se pide algo ENTONCES responden las reglas inmediatamente sin crashear.
    /// </summary>
    [Fact]
    public async Task Given_Both_Ollama_And_Gemini_Down_ConnectionRefused_When_SuggestRoutine_Then_Responds_From_DeterministicRules()
    {
        // Arrange
        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:59995", TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = "http://127.0.0.1:59996", ApiKey = "dummy-key", TimeoutSeconds = 1 }),
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

    /// <summary>
    /// Criterio de aceptación 2 (Variante Mixta 500 + Cuota Agotada): DADO Ollama 500 y Gemini 429
    /// CUANDO se pide algo ENTONCES responden las reglas determinísticas.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Http500_And_Gemini_Http429_When_SuggestRoutine_Then_Responds_From_DeterministicRules()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Ollama error"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(Response.Create().WithStatusCode(429).WithBody("Quota limit reached"));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 2 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "wiremock-key", TimeoutSeconds = 2 }),
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

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" && !log.IsValid && log.ValidationResult == "CuotaAgotada"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Criterio de aceptación 2 (Variante Timeouts Concurrentes): DADO Ollama y Gemini bloqueados por tiempo de respuesta
    /// CUANDO se pide algo ENTONCES ambos son abortados tras sus respectivos timeouts y responden las reglas determinísticas.
    /// </summary>
    [Fact]
    public async Task Given_Both_Ollama_And_Gemini_Down_Timeouts_When_SuggestRoutine_Then_Responds_From_DeterministicRules()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody(CreateOllamaSampleResponse("Lenta"))
                    .WithDelay(TimeSpan.FromSeconds(2)));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse("Gemini Lenta"))
                    .WithDelay(TimeSpan.FromSeconds(2)));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 1 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = _wireMockServer.Url!, ApiKey = "wiremock-key", TimeoutSeconds = 1 }),
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

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" && !log.IsValid && log.ValidationResult == "Timeout"), It.IsAny<CancellationToken>()), Times.Once);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" && !log.IsValid && log.ValidationResult == "Timeout"), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Escenario 3: Funcionamiento óptimo de Ollama (Sin conmutación a Gemini)

    /// <summary>
    /// Verifica que ante un Ollama saludable, Gemini no es contactado en absoluto.
    /// </summary>
    [Fact]
    public async Task Given_Ollama_Healthy_When_SuggestRoutine_Then_Responds_From_Ollama_And_Gemini_Is_Not_Called()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateOllamaSampleResponse("Fuerza - Modelo Ollama Local")));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithBody(CreateGeminiSampleResponse("Gemini")));

        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = _wireMockServer.Url!, TimeoutSeconds = 5 }),
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
        Assert.Equal("Ollama", result.ProviderUsed);
        Assert.Equal("Fuerza - Modelo Ollama Local", result.ProposedTitle);

        // Verificamos que Gemini NO recibió ninguna petición HTTP
        var geminiCalls = _wireMockServer.FindLogEntries(Request.Create().UsingPost().WithPath("/v1beta/models/*"));
        Assert.Empty(geminiCalls);
    }

    #endregion

    #region Escenario 4: Conmutación en Contenedor de Inyección de Dependencias (DI)

    /// <summary>
    /// Valida que el contenedor de dependencias configurado con HttpClientFactory conmute limpiamente de Ollama a Gemini.
    /// </summary>
    [Fact]
    public async Task Given_DiContainer_Full_Stack_When_Ollama_Down_Then_Switches_To_Gemini()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Ollama caido"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse("Fuerza - Gemini DI Container")));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_auditServiceMock.Object);

        services.Configure<OllamaOptions>(opts =>
        {
            opts.BaseUrl = _wireMockServer.Url!;
            opts.TimeoutSeconds = 2;
        });
        services.Configure<GeminiOptions>(opts =>
        {
            opts.BaseUrl = _wireMockServer.Url!;
            opts.ApiKey = "test-key";
            opts.TimeoutSeconds = 5;
        });

        services.AddHttpClient<OllamaClient>();
        services.AddHttpClient<GeminiClient>();
        services.AddScoped<RuleBasedAiClient>();
        services.AddScoped<IAiService, ResilientAiService>();

        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IAiService>();

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Equal("Fuerza - Gemini DI Container", result.ProposedTitle);
    }

    /// <summary>
    /// Valida que el contenedor de dependencias conmute limpiamente hasta el motor de reglas determinísticas cuando ambos caen.
    /// </summary>
    [Fact]
    public async Task Given_DiContainer_Full_Stack_When_Both_Ollama_And_Gemini_Down_Then_Switches_To_Rules()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Ollama 500"));

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Gemini 500"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_auditServiceMock.Object);

        services.Configure<OllamaOptions>(opts =>
        {
            opts.BaseUrl = _wireMockServer.Url!;
            opts.TimeoutSeconds = 2;
        });
        services.Configure<GeminiOptions>(opts =>
        {
            opts.BaseUrl = _wireMockServer.Url!;
            opts.ApiKey = "test-key";
            opts.TimeoutSeconds = 2;
        });

        services.AddHttpClient<OllamaClient>();
        services.AddHttpClient<GeminiClient>();
        services.AddScoped<RuleBasedAiClient>();
        services.AddScoped<IAiService, ResilientAiService>();

        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IAiService>();

        // Act
        var result = await orchestrator.SuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Reglas", result.ProviderUsed);
        Assert.Contains("Determinístico", result.ProposedTitle);
    }

    #endregion

    #region Escenario 5: Manejo estricto de CancellationToken

    /// <summary>
    /// Valida que una cancelación voluntaria iniciada por el cliente no sea interpretada como fallo de proveedor
    /// ni degrade a reglas determinísticas, sino que propague limpiamente OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task Given_CancellationToken_Already_Cancelled_When_SuggestRoutine_Then_Propagates_OperationCanceledException()
    {
        // Arrange
        var ollamaClient = new OllamaClient(
            new HttpClient(),
            Options.Create(new OllamaOptions { BaseUrl = "http://127.0.0.1:59999", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        var geminiClient = new GeminiClient(
            new HttpClient(),
            Options.Create(new GeminiOptions { BaseUrl = "http://127.0.0.1:59999", ApiKey = "test-key", TimeoutSeconds = 5 }),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        var rulesClient = new RuleBasedAiClient(_auditServiceMock.Object, NullLogger<RuleBasedAiClient>.Instance);
        var orchestrator = new ResilientAiService(ollamaClient, geminiClient, rulesClient, NullLogger<ResilientAiService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancelado antes de comenzar

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.SuggestRoutineAsync(_defaultRequest, cts.Token));
    }

    #endregion

    #region Helpers

    private static string CreateGeminiSampleResponse(string title)
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            tituloPropuesto = title,
            resumenJustificacion = "Propuesta simulada de Google Gemini para pruebas de conmutación.",
            ejercicios = new[]
            {
                new { ejercicioNombre = "Press de Banca Plano", series = 4, repeticiones = 6, porcentajeSugerido = 80.0 },
                new { ejercicioNombre = "Press Francés", series = 3, repeticiones = 10, porcentajeSugerido = 70.0 }
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
                promptTokenCount = 100,
                candidatesTokenCount = 200,
                totalTokenCount = 300
            }
        });
    }

    private static string CreateOllamaSampleResponse(string title)
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            tituloPropuesto = title,
            resumenJustificacion = "Propuesta simulada de Ollama para pruebas.",
            ejercicios = new[]
            {
                new { ejercicioNombre = "Press de Banca Plano", series = 4, repeticiones = 5, porcentajeSugerido = 82.5 }
            }
        });

        return JsonSerializer.Serialize(new
        {
            response = innerJson,
            prompt_eval_count = 50,
            eval_count = 90
        });
    }

    #endregion
}
