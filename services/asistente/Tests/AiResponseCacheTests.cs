namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para la caché en memoria de respuestas de IA (KAN-368).
/// Criterio central de aceptación:
/// "DADO el mismo pedido dos veces CUANDO se hace el segundo ENTONCES responde el cache, sin llamar a la IA."
/// </summary>
public sealed class AiResponseCacheTests
{
    private readonly IMemoryCache _memoryCache;
    private readonly AiResponseMemoryCache _cacheService;

    public AiResponseCacheTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new AiCacheOptions { Enabled = true, TtlMinutes = 30 });
        _cacheService = new AiResponseMemoryCache(_memoryCache, options, NullLogger<AiResponseMemoryCache>.Instance);
    }

    [Fact]
    public async Task Given_Same_Request_Twice_When_Second_Request_Executed_Then_Responds_From_Cache_Without_Calling_AI()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var request = new RoutineSuggestionRequestDto(
            AthleteId: athleteId,
            Goal: "Fuerza Máxima de Piernas",
            MuscleGroupEmphasis: ["Piernas", "Glúteos"]
        );

        var aiClientMock = new Mock<IAiClient>();
        aiClientMock.Setup(c => c.ProviderName).Returns("Ollama");

        var callCount = 0;
        aiClientMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) =>
            {
                callCount++;
                return new RoutineSuggestionResponseDto(
                    AthleteId: req.AthleteId,
                    ProposedTitle: "Rutina de Piernas",
                    JustificationSummary: "Enfoque en fuerza",
                    Exercises: [new SuggestedExerciseDto("Sentadilla trasera", 4, 6, 80m, Guid.NewGuid())],
                    ProviderUsed: "Ollama"
                );
            });

        var orchestrator = new ResilientAiService(
            providers: [aiClientMock.Object],
            semanticValidator: null,
            responseCache: _cacheService,
            logger: NullLogger<ResilientAiService>.Instance
        );

        // Act 1: Primer pedido (Cache Miss -> debe invocar a la IA)
        var response1 = await orchestrator.SuggestRoutineAsync(request);

        // Act 2: Segundo pedido idéntico (Cache Hit -> debe responder de memoria sin invocar a la IA)
        var response2 = await orchestrator.SuggestRoutineAsync(request);

        // Assert
        Assert.Equal(1, callCount); // ¡La IA sólo fue invocada una única vez!
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.Equal(response1.ProposedTitle, response2.ProposedTitle);
        Assert.Equal(response1.Exercises.Count, response2.Exercises.Count);
        Assert.Equal(athleteId, response2.AthleteId);
    }

    [Fact]
    public async Task Given_Same_Instructions_For_Different_Athletes_When_Second_Request_Executed_Then_Responds_From_Cache_With_Current_AthleteId()
    {
        // Arrange
        var athleteA = Guid.NewGuid();
        var athleteB = Guid.NewGuid();

        var request1 = new RoutineSuggestionRequestDto(athleteA, "Hipertrofia de Pectorales", ["Pectorales"]);
        var request2 = new RoutineSuggestionRequestDto(athleteB, "Hipertrofia de Pectorales", ["Pectorales"]);

        var callCount = 0;
        var aiClientMock = new Mock<IAiClient>();
        aiClientMock.Setup(c => c.ProviderName).Returns("Ollama");
        aiClientMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) =>
            {
                callCount++;
                return new RoutineSuggestionResponseDto(
                    AthleteId: req.AthleteId,
                    ProposedTitle: "Hipertrofia Pecho",
                    JustificationSummary: "Volumen medio",
                    Exercises: [new SuggestedExerciseDto("Press plano", 3, 10, 70m, Guid.NewGuid())],
                    ProviderUsed: "Ollama"
                );
            });

        var orchestrator = new ResilientAiService(
            providers: [aiClientMock.Object],
            semanticValidator: null,
            responseCache: _cacheService,
            logger: NullLogger<ResilientAiService>.Instance
        );

        // Act
        var res1 = await orchestrator.SuggestRoutineAsync(request1);
        var res2 = await orchestrator.SuggestRoutineAsync(request2);

        // Assert
        Assert.Equal(1, callCount); // Se reutilizó la rutina sin llamar a la IA
        Assert.Equal(athleteA, res1.AthleteId);
        Assert.Equal(athleteB, res2.AthleteId); // Se adaptó el AthleteId al del segundo atleta
    }

    [Fact]
    public void Given_Muscle_Groups_In_Different_Order_When_Computing_Hash_Then_Produces_Identical_Hash()
    {
        // Arrange
        var hash1 = _cacheService.ComputePromptHash("Fuerza Máxima", ["Piernas", "Core", "Espalda"]);
        var hash2 = _cacheService.ComputePromptHash("   fuerza máxima   ", ["Espalda", "Piernas", "Core"]);

        // Assert: El orden no debe alterar el hash canonizado
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Given_Different_Goal_When_Computing_Hash_Then_Produces_Different_Hash()
    {
        // Arrange
        var hash1 = _cacheService.ComputePromptHash("Hipertrofia", ["Piernas"]);
        var hash2 = _cacheService.ComputePromptHash("Fuerza Máxima", ["Piernas"]);

        // Assert: Distinto objetivo debe generar distinta clave
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task Given_Expired_Cache_Entry_When_Request_Executed_Then_Calls_AI_Again()
    {
        // Arrange
        var request = new RoutineSuggestionRequestDto(Guid.NewGuid(), "Potencia", ["Piernas"]);
        var callCount = 0;

        var aiClientMock = new Mock<IAiClient>();
        aiClientMock.Setup(c => c.ProviderName).Returns("Ollama");
        aiClientMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) =>
            {
                callCount++;
                return new RoutineSuggestionResponseDto(req.AthleteId, "Potencia", "Pliometría", [], "Ollama");
            });

        var orchestrator = new ResilientAiService(
            providers: [aiClientMock.Object],
            semanticValidator: null,
            responseCache: _cacheService,
            logger: NullLogger<ResilientAiService>.Instance
        );

        // Act 1: Primer pedido guarda con TTL corto
        var hash = _cacheService.ComputePromptHash(request.Goal, request.MuscleGroupEmphasis);
        var initial = await orchestrator.SuggestRoutineAsync(request);
        _cacheService.Set(hash, initial, TimeSpan.FromMilliseconds(50));

        // Esperamos a que expire el tiempo de vida en caché
        await Task.Delay(100);

        // Act 2: Segundo pedido tras expiración
        await orchestrator.SuggestRoutineAsync(request);

        // Assert: Al haber expirado, volvió a consultar al proveedor de IA
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Given_Cache_Disabled_When_Requests_Executed_Then_Always_Invokes_AI()
    {
        // Arrange: Caché desactivada en opciones
        var disabledOptions = Options.Create(new AiCacheOptions { Enabled = false });
        var disabledCache = new AiResponseMemoryCache(_memoryCache, disabledOptions, NullLogger<AiResponseMemoryCache>.Instance);

        var request = new RoutineSuggestionRequestDto(Guid.NewGuid(), "Movilidad", ["Cadera"]);
        var callCount = 0;

        var aiClientMock = new Mock<IAiClient>();
        aiClientMock.Setup(c => c.ProviderName).Returns("Ollama");
        aiClientMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) =>
            {
                callCount++;
                return new RoutineSuggestionResponseDto(req.AthleteId, "Movilidad", "Estiramientos", [], "Ollama");
            });

        var orchestrator = new ResilientAiService(
            providers: [aiClientMock.Object],
            semanticValidator: null,
            responseCache: disabledCache,
            logger: NullLogger<ResilientAiService>.Instance
        );

        // Act
        await orchestrator.SuggestRoutineAsync(request);
        await orchestrator.SuggestRoutineAsync(request);

        // Assert: Ambas peticiones ejecutaron la IA porque la caché está deshabilitada
        Assert.Equal(2, callCount);
    }
}
