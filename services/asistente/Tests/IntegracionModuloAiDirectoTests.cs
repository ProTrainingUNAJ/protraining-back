using Application.Interfaces.Persistence;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
namespace Asistente.Tests;

using API.Controllers;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

/// <summary>
/// Pruebas automatizadas de la tarea #086 · KAN-381:
/// Integrar el módulo de IA en el Asistente (llamada directa al proveedor).
/// Valida la comunicación directa, cumplimiento contractual y validaciones de entrada.
/// </summary>
public sealed class IntegracionModuloAiDirectoTests
{
    private static readonly Guid TestAthleteId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
    private static readonly Guid SentadillaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("Ollama")]
    [InlineData("Gemini")]
    [InlineData("Reglas")]
    public async Task Given_ValidRequest_When_AiProviderResponds_Then_Returns_200Ok_And_Accurate_Provider(string providerName)
    {
        // Arrange
        var mockAiService = new Mock<IAiService>();
        mockAiService
            .Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) => new RoutineSuggestionResponseDto(
                AthleteId: req.AthleteId,
                ProposedTitle: $"Rutina generada por {providerName}",
                JustificationSummary: $"Respuesta directa del proveedor {providerName} para {req.Goal}",
                Exercises:
                [
                    new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 8, 75.0m, SentadillaId, false)
                ],
                ProviderUsed: providerName
            ));

        var useCase = new SuggestRoutineUseCase(mockAiService.Object);
        var controller = new AssistantController(useCase, new GetAthleteDecisionsQueryHandler(Mock.Of<IPfDecisionRepository>(), Mock.Of<IStructureAthleteClient>()));

        var request = new RoutineSuggestionRequestDto(
            AthleteId: TestAthleteId,
            Goal: "Fuerza y potencia",
            MuscleGroupEmphasis: ["Piernas"]
        );

        // Act
        var result = await controller.SuggestRoutine(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, okResult.StatusCode);

        var response = Assert.IsType<RoutineSuggestionResponseDto>(okResult.Value);
        Assert.Equal(TestAthleteId, response.AthleteId);
        Assert.Equal(providerName, response.ProviderUsed);
        Assert.NotEmpty(response.ProposedTitle);
        Assert.NotEmpty(response.JustificationSummary);
        Assert.Single(response.Exercises);
        Assert.Equal(SentadillaId, response.Exercises[0].ExerciseId);

        mockAiService.Verify(
            s => s.SuggestRoutineAsync(
                It.Is<RoutineSuggestionRequestDto>(r => r.AthleteId == TestAthleteId && r.Goal == "Fuerza y potencia"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Given_RequestWithUntrimmedGoal_When_Executed_Then_GoalIsTrimmedBeforeCallingAiModule()
    {
        // Arrange
        RoutineSuggestionRequestDto? capturedRequest = null;
        var mockAiService = new Mock<IAiService>();
        mockAiService
            .Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<RoutineSuggestionRequestDto, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: TestAthleteId,
                ProposedTitle: "Rutina",
                JustificationSummary: "Justificación",
                Exercises: [],
                ProviderUsed: "Ollama"
            ));

        var useCase = new SuggestRoutineUseCase(mockAiService.Object);
        var request = new RoutineSuggestionRequestDto(
            AthleteId: TestAthleteId,
            Goal: "   Hipertrofia de Torso   ",
            MuscleGroupEmphasis: ["Pecho", "Espalda"]
        );

        // Act
        var response = await useCase.ExecuteAsync(request);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("Hipertrofia de Torso", capturedRequest.Goal);
        Assert.Equal(TestAthleteId, response.AthleteId);
    }

    [Fact]
    public async Task Given_EmptyAthleteId_When_Executed_Then_ThrowsDomainException_WithoutInvokingAi()
    {
        // Arrange
        var mockAiService = new Mock<IAiService>();
        var useCase = new SuggestRoutineUseCase(mockAiService.Object);

        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.Empty,
            Goal: "Resistencia aeróbica"
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => useCase.ExecuteAsync(request));
        Assert.Contains("El identificador del atleta es obligatorio", ex.Message);

        mockAiService.Verify(
            s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Given_EmptyOrWhitespaceGoal_When_Executed_Then_ThrowsDomainException_WithoutInvokingAi()
    {
        // Arrange
        var mockAiService = new Mock<IAiService>();
        var useCase = new SuggestRoutineUseCase(mockAiService.Object);

        var request = new RoutineSuggestionRequestDto(
            AthleteId: TestAthleteId,
            Goal: "\t  \n  "
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() => useCase.ExecuteAsync(request));
        Assert.Contains("El objetivo de entrenamiento es obligatorio", ex.Message);

        mockAiService.Verify(
            s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Given_AiModuleResponse_When_ExercisesReturned_Then_SetsAndRepsAndPercentagesArePositive()
    {
        // Arrange
        var mockAiService = new Mock<IAiService>();
        mockAiService
            .Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutineSuggestionResponseDto(
                AthleteId: TestAthleteId,
                ProposedTitle: "Sesión de Fuerza",
                JustificationSummary: "Criterio de progresión lineal",
                Exercises:
                [
                    new SuggestedExerciseDto("Press de Banca", 4, 6, 80.0m, Guid.NewGuid(), false),
                    new SuggestedExerciseDto("Remo con Barra", 3, 10, 70.0m, Guid.NewGuid(), false)
                ],
                ProviderUsed: "Ollama"
            ));

        var useCase = new SuggestRoutineUseCase(mockAiService.Object);
        var request = new RoutineSuggestionRequestDto(TestAthleteId, "Fuerza");

        // Act
        var response = await useCase.ExecuteAsync(request);

        // Assert
        Assert.Equal(2, response.Exercises.Count);
        foreach (var ex in response.Exercises)
        {
            Assert.True(ex.Sets > 0, "Las series deben ser positivas.");
            Assert.True(ex.Reps > 0, "Las repeticiones deben ser positivas.");
            Assert.True(ex.SuggestedPercentage > 0, "El porcentaje debe ser positivo.");
        }
    }

    [Fact]
    public async Task Given_RealHttpOllamaServer_When_SuggestRoutineDirectly_Then_CompletesFullNetworkCycleAndAudits()
    {
        // Arrange: Iniciar servidor HTTP real en loopback para simular Ollama en vivo (#086)
        using var server = WireMock.Server.WireMockServer.Start();
        const string routineJson = "{\"tituloPropuesto\":\"Fuerza Máxima Real\",\"resumenJustificacion\":\"Planificación directa\",\"ejercicios\":[{\"ejercicioNombre\":\"Sentadilla\",\"series\":4,\"repeticiones\":6,\"porcentajeSugerido\":85.0}]}";

        var payload = new
        {
            model = "llama3.2:1b",
            response = routineJson,
            done = true,
            prompt_eval_count = 45,
            eval_count = 110
        };

        server
            .Given(WireMock.RequestBuilders.Request.Create().UsingPost().WithPath("/api/generate"))
            .RespondWith(
                WireMock.ResponseBuilders.Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(System.Text.Json.JsonSerializer.Serialize(payload))
            );

        var auditMock = new Mock<IAiAuditService>();
        var httpClient = new HttpClient { BaseAddress = new Uri(server.Url!) };
        var options = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Services.Ai.Ollama.OllamaOptions
        {
            BaseUrl = server.Url!,
            Model = "llama3.2:1b",
            TimeoutSeconds = 15
        });

        var ollamaClient = new Infrastructure.Services.Ai.Ollama.OllamaClient(
            httpClient,
            options,
            auditMock.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.Services.Ai.Ollama.OllamaClient>.Instance
        );

        var resilientAiService = new Infrastructure.Services.Ai.ResilientAiService(
            providers: [ollamaClient],
            semanticValidator: null,
            responseCache: null,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.Services.Ai.ResilientAiService>.Instance
        );

        var useCase = new SuggestRoutineUseCase(resilientAiService);
        var controller = new AssistantController(useCase, new GetAthleteDecisionsQueryHandler(Mock.Of<IPfDecisionRepository>(), Mock.Of<IStructureAthleteClient>()));
        var request = new RoutineSuggestionRequestDto(TestAthleteId, "Fuerza Máxima");

        // Act: Llamada real de extremo a extremo a través del socket HTTP
        var result = await controller.SuggestRoutine(request, CancellationToken.None);

        // Assert: Respuesta 200 OK generada directamente desde el payload del servidor HTTP
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, okResult.StatusCode);

        var response = Assert.IsType<RoutineSuggestionResponseDto>(okResult.Value);
        Assert.Equal(TestAthleteId, response.AthleteId);
        Assert.Equal("Ollama", response.ProviderUsed);
        Assert.Equal("Fuerza Máxima Real", response.ProposedTitle);
        Assert.Single(response.Exercises);
        Assert.Equal("Sentadilla", response.Exercises[0].ExerciseName);
        Assert.Equal(4, response.Exercises[0].Sets);
        Assert.Equal(6, response.Exercises[0].Reps);

        // Verifica que la llamada HTTP real haya sido registrada en la auditoría con sus tokens
        auditMock.Verify(a => a.LogAsync(It.Is<Domain.Entities.AiLog>(l =>
            l.Provider == "Ollama" &&
            l.IsValid &&
            l.PromptTokens == 45 &&
            l.CompletionTokens == 110
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
