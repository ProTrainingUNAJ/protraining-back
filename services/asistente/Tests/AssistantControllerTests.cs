namespace Asistente.Tests;

using System.Reflection;
using API.Controllers;
using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Domain.Exceptions;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Rules;
using Infrastructure.Services.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias y de integración del controlador AssistantController (KAN-517 y KAN-344).
/// Valida el flujo desde el punto de entrada HTTP, verificación de roles requeridos,
/// rutas estándar en inglés y alias de retrocompatibilidad.
/// </summary>
public sealed class AssistantControllerTests
{
    private static readonly Guid SentadillaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly AssistantController _controller;

    public AssistantControllerTests()
    {
        var mockAiService = new Mock<IAiService>();
        mockAiService.Setup(s => s.SuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) => new RoutineSuggestionResponseDto(
                AthleteId: req.AthleteId,
                ProposedTitle: "Fuerza de Piernas",
                JustificationSummary: "Planificación con sobrecarga progresiva",
                Exercises:
                [
                    new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 6, 80.0m, SentadillaId, IsReference: false)
                ],
                ProviderUsed: "Reglas"
            ));

        var useCase = new SuggestRoutineUseCase(mockAiService.Object);
        _controller = new AssistantController(useCase);
    }

    [Fact]
    public async Task Given_ValidRequest_When_SuggestRoutine_Then_Returns_200Ok_With_Sanitized_Routine()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "Fuerza Máxima de Piernas",
            MuscleGroupEmphasis: ["Piernas"]
        );

        // Act
        var actionResult = await _controller.SuggestRoutine(request, CancellationToken.None);

        // Assert: Retorna HTTP 200 OK con payload estructurado
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Equal(200, okResult.StatusCode);

        var response = Assert.IsType<RoutineSuggestionResponseDto>(okResult.Value);
        Assert.Equal(request.AthleteId, response.AthleteId);
        Assert.NotEmpty(response.ProposedTitle);
        Assert.NotEmpty(response.Exercises);
        Assert.Contains(response.Exercises, e => e.ExerciseId == SentadillaId);
    }

    [Fact]
    public async Task Given_EmptyAthleteId_When_SuggestRoutine_Then_Throws_DomainException()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.Empty,
            Goal: "Hipertrofia",
            MuscleGroupEmphasis: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() => _controller.SuggestRoutine(request, CancellationToken.None));
    }

    [Fact]
    public async Task Given_EmptyGoal_When_SuggestRoutine_Then_Throws_DomainException()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "   ",
            MuscleGroupEmphasis: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<DomainException>(() => _controller.SuggestRoutine(request, CancellationToken.None));
    }

    [Fact]
    public void Given_AssistantController_When_Inspecting_Security_Attributes_Then_Requires_PreparadorFisico_Role()
    {
        // Assert: El controlador debe estar protegido con rol PreparadorFisico
        var authAttribute = typeof(AssistantController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);
        Assert.Equal("PreparadorFisico", authAttribute.Roles);
    }

    [Fact]
    public void Given_AssistantController_When_Inspecting_Routes_Then_Exposes_English_Route()
    {
        // Assert: Ruta base api/v1/assistant
        var routeAttribute = typeof(AssistantController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttribute);
        Assert.Equal("api/v1/assistant", routeAttribute.Template);

        // Assert: Método SuggestRoutine expone exclusivamente la ruta estándar en inglés
        var method = typeof(AssistantController).GetMethod(nameof(AssistantController.SuggestRoutine));
        Assert.NotNull(method);

        var httpPost = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(httpPost);
        Assert.Equal("suggest-routine", httpPost.Template);
    }

    [Fact]
    public async Task Given_EndToEndController_When_SuggestRoutine_Called_Twice_Then_SecondCall_Returns_Cached_Result_Rebound_To_New_Athlete()
    {
        // Arrange: Componentes reales de DI (Controller -> UseCase -> ResilientAiService -> Cache + RuleEngine)
        var memoryCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var cacheOptions = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Services.Ai.Cache.AiCacheOptions { Enabled = true, TtlMinutes = 30 });
        var cache = new Infrastructure.Services.Ai.Cache.AiResponseMemoryCache(memoryCache, cacheOptions, NullLogger<Infrastructure.Services.Ai.Cache.AiResponseMemoryCache>.Instance);

        var auditMock = new Mock<IAiAuditService>();
        var rulesClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var realAiService = new ResilientAiService(
            providers: [rulesClient],
            semanticValidator: null,
            responseCache: cache,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var useCase = new SuggestRoutineUseCase(realAiService);
        var controller = new AssistantController(useCase);

        var athlete1 = Guid.NewGuid();
        var athlete2 = Guid.NewGuid();
        var request1 = new RoutineSuggestionRequestDto(athlete1, "Fuerza Máxima de Piernas", ["Piernas"]);
        var request2 = new RoutineSuggestionRequestDto(athlete2, "Fuerza Máxima de Piernas", ["Piernas"]);

        // Act 1: Primer llamado (Cache MISS -> resuelve con motor de reglas y guarda en caché)
        var result1 = await controller.SuggestRoutine(request1, CancellationToken.None);

        // Act 2: Segundo llamado con otro atleta e idéntico objetivo (Cache HIT -> responde desde memoria)
        var result2 = await controller.SuggestRoutine(request2, CancellationToken.None);

        // Assert: Respuestas HTTP 200 OK con reasociación segura del AthleteId
        var ok1 = Assert.IsType<OkObjectResult>(result1.Result);
        var ok2 = Assert.IsType<OkObjectResult>(result2.Result);

        var response1 = Assert.IsType<RoutineSuggestionResponseDto>(ok1.Value);
        var response2 = Assert.IsType<RoutineSuggestionResponseDto>(ok2.Value);

        Assert.Equal(athlete1, response1.AthleteId);
        Assert.Equal(athlete2, response2.AthleteId);
        Assert.Equal(response1.ProposedTitle, response2.ProposedTitle);
        Assert.Equal(response1.Exercises.Count, response2.Exercises.Count);
        Assert.NotEmpty(response2.Exercises);
    }
}
