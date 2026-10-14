using System.Text.Json;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Asistente.Tests;

public sealed class GeminiClientTests : IDisposable
{
    private readonly WireMockServer _wireMockServer;
    private readonly Mock<IAiAuditService> _auditServiceMock;
    private readonly RoutineSuggestionRequestDto _defaultRequest;

    public GeminiClientTests()
    {
        _wireMockServer = WireMockServer.Start();
        _auditServiceMock = new Mock<IAiAuditService>();
        _defaultRequest = new RoutineSuggestionRequestDto(
            AthleteId: Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            Goal: "Hipertrofia",
            MuscleGroupEmphasis: new List<string> { "Pecho", "Tríceps" }
        );
    }

    public void Dispose()
    {
        _wireMockServer.Stop();
        _wireMockServer.Dispose();
    }

    [Fact]
    public async Task Given_Gemini_Request_When_Called_Then_Sends_ApiKey_In_Header_XGoogApiKey_And_Not_In_QueryParam()
    {
        // Arrange
        const string secretKey = "super-secret-api-key-12345";
        var sampleResponse = CreateGeminiSampleResponse();

        _wireMockServer
            .Given(Request.Create()
                .UsingPost()
                .WithPath("/v1beta/models/gemini-flash-lite-latest:generateContent")
                .WithHeader("x-goog-api-key", secretKey))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(sampleResponse));

        var options = new GeminiOptions
        {
            BaseUrl = _wireMockServer.Url!,
            ApiKey = secretKey,
            Model = "gemini-flash-lite-latest",
            TimeoutSeconds = 5
        };

        var client = new GeminiClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);

        // Verificar que no se haya enviado ninguna petición con 'key=' en la URL y que contenga el header
        var receivedRequests = _wireMockServer.FindLogEntries(Request.Create().UsingPost());
        Assert.NotEmpty(receivedRequests);
        foreach (var entry in receivedRequests)
        {
            Assert.DoesNotContain("key=", entry.RequestMessage.Url);
            Assert.True(entry.RequestMessage.Headers?.ContainsKey("x-goog-api-key") == true);
            Assert.Contains(secretKey, entry.RequestMessage.Headers!["x-goog-api-key"]);
        }
    }

    [Fact]
    public async Task Given_Gemini_Returns_Valid_Json_When_SuggestRoutine_Then_Returns_Routine_And_Audits_Tokens()
    {
        // Arrange
        var sampleResponse = CreateGeminiSampleResponse();

        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(sampleResponse));

        var options = new GeminiOptions
        {
            BaseUrl = _wireMockServer.Url!,
            ApiKey = "test-key",
            Model = "gemini-flash-lite-latest",
            TimeoutSeconds = 5
        };

        var client = new GeminiClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Gemini", result.ProviderUsed);
        Assert.Equal("Hipertrofia - Gemini", result.ProposedTitle);
        Assert.Single(result.Exercises);
        Assert.Equal("Press Banca", result.Exercises[0].ExerciseName);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" &&
            log.IsValid &&
            log.ValidationResult == "Valido" &&
            log.PromptTokens == 150 &&
            log.CompletionTokens == 200 &&
            log.TotalTokens == 350 &&
            log.AthleteId == _defaultRequest.AthleteId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Gemini_Returns_Http429_When_SuggestRoutine_Then_Returns_Null_And_Audits_CuotaAgotada()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(429)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("{\"error\":{\"code\":429,\"message\":\"Resource exhausted\"}}"));

        var options = new GeminiOptions
        {
            BaseUrl = _wireMockServer.Url!,
            ApiKey = "test-key",
            Model = "gemini-flash-lite-latest",
            TimeoutSeconds = 5
        };

        var client = new GeminiClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" &&
            !log.IsValid &&
            log.ValidationResult == "CuotaAgotada" &&
            log.ErrorMessage!.Contains("429")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Gemini_TimesOut_When_SuggestRoutine_Then_Aborts_And_Audits_Timeout()
    {
        // Arrange: timeout de 1s con delay de 2s en WireMock
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(CreateGeminiSampleResponse())
                    .WithDelay(TimeSpan.FromSeconds(2)));

        var options = new GeminiOptions
        {
            BaseUrl = _wireMockServer.Url!,
            ApiKey = "test-key",
            Model = "gemini-flash-lite-latest",
            TimeoutSeconds = 1
        };

        var client = new GeminiClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" &&
            !log.IsValid &&
            log.ValidationResult == "Timeout" &&
            log.ErrorMessage!.Contains("1")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_Gemini_Returns_Http500_When_SuggestRoutine_Then_Returns_Null_And_Audits_ErrorHttp()
    {
        // Arrange
        _wireMockServer
            .Given(Request.Create().UsingPost().WithPath("/v1beta/models/*"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(500)
                    .WithBody("Internal Server Error"));

        var options = new GeminiOptions
        {
            BaseUrl = _wireMockServer.Url!,
            ApiKey = "test-key",
            Model = "gemini-flash-lite-latest",
            TimeoutSeconds = 5
        };

        var client = new GeminiClient(
            new HttpClient(),
            Options.Create(options),
            _auditServiceMock.Object,
            NullLogger<GeminiClient>.Instance);

        // Act
        var result = await client.TrySuggestRoutineAsync(_defaultRequest);

        // Assert
        Assert.Null(result);

        _auditServiceMock.Verify(a => a.LogAsync(It.Is<AiLog>(log =>
            log.Provider == "Gemini" &&
            !log.IsValid &&
            log.ValidationResult == "ErrorHttp" &&
            log.ErrorMessage!.Contains("500")), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string CreateGeminiSampleResponse()
    {
        var innerJson = JsonSerializer.Serialize(new
        {
            tituloPropuesto = "Hipertrofia - Gemini",
            resumenJustificacion = "Propuesta simulada de Gemini.",
            ejercicios = new[]
            {
                new { ejercicioNombre = "Press Banca", series = 4, repeticiones = 8, porcentajeSugerido = 75.0 }
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
                promptTokenCount = 150,
                candidatesTokenCount = 200,
                totalTokenCount = 350
            }
        });
    }
}
