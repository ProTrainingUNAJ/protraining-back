using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Asistente.Tests;

public sealed class OllamaClientTests : IDisposable
{
    private readonly WireMockServer _wireMockServer;
    private readonly Mock<IAiAuditService> _auditServiceMock;
    private readonly RoutineSuggestionRequestDto _defaultRequest;

    public OllamaClientTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditServiceMock = new Mock<IAiAuditService>();
        _defaultRequest = new RoutineSuggestionRequestDto(
            AthleteId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Goal: "Fuerza y Potencia",
            MuscleGroupEmphasis: new List<string> { "Piernas", "Core" }
        );
    }

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
    }

    [Fact]
    public async Task Given_Ollama_Returns_Valid_Json_When_SuggestRoutine_Then_Returns_Routine_With_Ollama_Provider_And_Audits_Tokens()
    {
        // Arrange
        const string routineJson = "{\"tituloPropuesto\":\"Fuerza Piernas Ollama\",\"resumenJustificacion\":\"Enfoque sentadilla y core\",\"ejercicios\":[{\"ejercicioNombre\":\"Sentadilla Trasera\",\"series\":4,\"repeticiones\":6,\"porcentajeSugerido\":82.5},{\"ejercicioNombre\":\"Plancha isométrica\",\"series\":3,\"repeticiones\":60,\"porcentajeSugerido\":50.0}]}";

        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            created_at = "2026-10-12T19:00:00Z",
            response = routineJson,
            done = true,
            prompt_eval_count = 52,
            eval_count = 140
        };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Ollama", result.ProviderUsed);
        Assert.Equal("Fuerza Piernas Ollama", result.ProposedTitle);
        Assert.Equal("Enfoque sentadilla y core", result.JustificationSummary);
        Assert.Equal(2, result.Exercises.Count);
        Assert.Equal("Sentadilla Trasera", result.Exercises[0].ExerciseName);
        Assert.Equal(4, result.Exercises[0].Sets);
        Assert.Equal(6, result.Exercises[0].Reps);
        Assert.Equal(82.5m, result.Exercises[0].SuggestedPercentage);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            log.IsValid &&
            log.ValidationResult == "Valido" &&
            log.PromptTokens == 52 &&
            log.CompletionTokens == 140 &&
            log.TotalTokens == 192 &&
            log.AthleteId == _defaultRequest.AthleteId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Ollama_Request_When_Called_Then_Sends_Format_Json_And_Stream_False()
    {
        // Arrange
        const string routineJson = "{\"tituloPropuesto\":\"Rutina Test\",\"resumenJustificacion\":\"Justificación test\",\"ejercicios\":[{\"ejercicioNombre\":\"Press\",\"series\":3,\"repeticiones\":10,\"porcentajeSugerido\":70}]}";
        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            response = routineJson,
            done = true
        };

        _wireMockServer
            .Given(Request.Create()
                .UsingPost()
                .WithPath("/api/generate")
                .WithBody(b => b != null && b.Contains("\"format\":\"json\"") && b.Contains("\"stream\":false") && b.Contains("\"model\":\"llama3.2:1b\"")))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Ollama", result.ProviderUsed);
    }

    [Fact]
    public async Task Given_Ollama_Returns_Json_Wrapped_In_Markdown_Fences_When_SuggestRoutine_Then_Sanitizes_And_Succeeds()
    {
        // Arrange
        const string wrappedJson = "```json\n{\"tituloPropuesto\":\"Rutina Con Fences\",\"resumenJustificacion\":\"Limpia fences\",\"ejercicios\":[{\"ejercicioNombre\":\"Dominadas\",\"series\":3,\"repeticiones\":8,\"porcentajeSugerido\":70.0}]}\n```";
        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            response = wrappedJson,
            done = true
        };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Rutina Con Fences", result.ProposedTitle);
        Assert.Single(result.Exercises);
        Assert.Equal("Dominadas", result.Exercises[0].ExerciseName);
    }

    [Fact]
    public async Task Given_Ollama_Takes_Longer_Than_Timeout_When_SuggestRoutine_Then_Aborts_And_Logs_Timeout()
    {
        // Arrange: configuramos 1 segundo de timeout y WireMock con delay de 2 segundos
        const string routineJson = "{\"tituloPropuesto\":\"Lenta\",\"resumenJustificacion\":\"Demora\",\"ejercicios\":[{\"ejercicioNombre\":\"Paseo granjero\",\"series\":3,\"repeticiones\":1,\"porcentajeSugerido\":50}]}";
        var ollamaResponse = new { model = "llama3.2:1b", response = routineJson, done = true };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse))
                    .WithDelay(TimeSpan.FromSeconds(2)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 1 // Timeout corto para test veloz
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "Timeout" &&
            log.ErrorMessage!.Contains("1")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Ollama_Returns_Http500_When_SuggestRoutine_Then_Returns_Null_And_Logs_ErrorHttp()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500)
                    .WithBody("Internal Server Error"));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "ErrorHttp" &&
            log.ErrorMessage!.Contains("500")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Ollama_Returns_Malformed_Json_When_SuggestRoutine_Then_Returns_Null_And_Logs_JsonInvalido()
    {
        // Arrange
        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            response = "Respuesta en texto libre sin formato JSON",
            done = true
        };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "JsonInvalido"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Ollama_Returns_Empty_Response_When_SuggestRoutine_Then_Returns_Null_And_Logs_RespuestaVacia()
    {
        // Arrange
        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            response = "   ",
            done = true
        };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "RespuestaVacia"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Ollama_Returns_Json_With_Series_As_String_When_SuggestRoutine_Then_Returns_Null_And_Logs_SchemaInvalido()
    {
        // Arrange
        const string invalidSchemaJson = "{\"tituloPropuesto\":\"Rutina Invalida\",\"resumenJustificacion\":\"Series como texto\",\"ejercicios\":[{\"ejercicioNombre\":\"Sentadilla\",\"series\":\"tres\",\"repeticiones\":10,\"porcentajeSugerido\":70.0}]}";
        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            response = invalidSchemaJson,
            done = true
        };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "SchemaInvalido" &&
            log.ErrorMessage!.Contains("series")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Ollama_Returns_Json_With_Percentage_150_When_SuggestRoutine_Then_Returns_Null_And_Logs_SchemaInvalido()
    {
        // Arrange
        const string outOfRangeJson = "{\"tituloPropuesto\":\"Rutina Rango Invalido\",\"resumenJustificacion\":\"Porcentaje fuera de rango\",\"ejercicios\":[{\"ejercicioNombre\":\"Press Banca\",\"series\":3,\"repeticiones\":10,\"porcentajeSugerido\":150.0}]}";
        var ollamaResponse = new
        {
            model = "llama3.2:1b",
            response = outOfRangeJson,
            done = true
        };

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(ollamaResponse)));

        var options = new OllamaOptions
        {
            BaseUrl = _wireMockServer.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        };

        var client = new OllamaClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<OllamaClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Ollama" &&
            !log.IsValid &&
            log.ValidationResult == "SchemaInvalido" &&
            log.ErrorMessage!.Contains("porcentajeSugerido")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
