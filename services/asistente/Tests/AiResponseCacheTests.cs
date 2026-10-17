namespace Asistente.Tests;

using Application.Common.Ai;
using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Cache;
using Infrastructure.Services.Ai.Rules;
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

    [Fact]
    public async Task Given_DeterministicRules_Response_When_Executed_Then_Not_Cached()
    {
        // Arrange: Motor de reglas determinísticas como proveedor de respaldo
        var auditMock = new Mock<IAiAuditService>();
        var rulesClient = new RuleBasedAiClient(auditMock.Object, NullLogger<RuleBasedAiClient>.Instance);

        var orchestrator = new ResilientAiService(
            providers: [rulesClient],
            semanticValidator: null,
            responseCache: _cacheService,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(Guid.NewGuid(), "Fuerza Máxima", ["Piernas"]);

        // Act 1: Primer pedido resuelto por reglas determinísticas
        var response1 = await orchestrator.SuggestRoutineAsync(request);

        // Act 2: Segundo pedido idéntico
        var response2 = await orchestrator.SuggestRoutineAsync(request);

        // Assert: La respuesta de reglas NO debe quedar guardada en memoria
        // para que ante una recuperación de Ollama/Gemini, no quede bloqueado por 30 minutos.
        var promptHash = _cacheService.ComputePromptHash(request.Goal, request.MuscleGroupEmphasis);
        var isCached = _cacheService.TryGet(promptHash, out _);

        Assert.False(isCached);
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.Equal("Reglas", response1.ProviderUsed);
        Assert.Equal("Reglas", response2.ProviderUsed);
    }

    [Fact]
    public async Task Given_Different_Pfs_With_Same_Goal_When_Executed_Then_Cache_Is_Isolated_By_Pf()
    {
        // Arrange: Dos PFs distintos consultando el mismo objetivo
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(pfA);

        var callCount = 0;
        var aiClientMock = new Mock<IAiClient>();
        aiClientMock.Setup(c => c.ProviderName).Returns("Ollama");
        aiClientMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) =>
            {
                callCount++;
                return new RoutineSuggestionResponseDto(
                    AthleteId: req.AthleteId,
                    ProposedTitle: "Rutina Isquios",
                    JustificationSummary: "Rehabilitación específica",
                    Exercises: [new SuggestedExerciseDto("Curl femoral", 3, 12, 60m, Guid.NewGuid())],
                    ProviderUsed: "Ollama"
                );
            });

        var orchestrator = new ResilientAiService(
            providers: [aiClientMock.Object],
            semanticValidator: null,
            responseCache: _cacheService,
            currentUser: currentUserMock.Object,
            auditService: null,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(Guid.NewGuid(), "Rehabilitación isquios", ["Piernas"]);

        // Act 1: PF A realiza la consulta (Cache Miss -> llama a la IA)
        var responseA = await orchestrator.SuggestRoutineAsync(request);

        // Act 2: PF B realiza EXACTAMENTE el mismo pedido (debe aislarse y llamar a la IA de nuevo)
        currentUserMock.Setup(u => u.PfId).Returns(pfB);
        var responseB = await orchestrator.SuggestRoutineAsync(request);

        // Act 3: PF A repite su pedido (debe recuperar su propio caché)
        currentUserMock.Setup(u => u.PfId).Returns(pfA);
        var responseA2 = await orchestrator.SuggestRoutineAsync(request);

        // Assert:
        // Se llamó a la IA 2 veces (una para PF A y otra para PF B).
        // La 3ra petición de PF A no llamó a la IA porque respondió desde su caché aislado.
        Assert.Equal(2, callCount);
        Assert.NotNull(responseA);
        Assert.NotNull(responseB);
        Assert.NotNull(responseA2);
        Assert.Equal("Ollama", responseA.ProviderUsed);
        Assert.Equal("Ollama", responseB.ProviderUsed);
        Assert.Equal("Ollama (cache)", responseA2.ProviderUsed);
    }

    [Fact]
    public void Given_Different_Pfs_When_Computing_Hash_Then_Produces_Different_Hash()
    {
        // Arrange
        var pf1 = Guid.NewGuid();
        var pf2 = Guid.NewGuid();

        var hash1 = _cacheService.ComputePromptHash("Fuerza", ["Piernas"], pf1);
        var hash2 = _cacheService.ComputePromptHash("Fuerza", ["Piernas"], pf2);
        var hashGlobal = _cacheService.ComputePromptHash("Fuerza", ["Piernas"], null);

        // Assert: Distinto PF genera claves distintas, garantizando aislamiento en IMemoryCache
        Assert.NotEqual(hash1, hash2);
        Assert.NotEqual(hash1, hashGlobal);
    }

    [Fact]
    public async Task Given_Cached_Response_When_Returned_Then_ProviderUsed_Indicates_Cache_And_Audit_Is_Logged()
    {
        // Arrange
        var auditMock = new Mock<IAiAuditService>();
        var aiClientMock = new Mock<IAiClient>();
        aiClientMock.Setup(c => c.ProviderName).Returns("Ollama");
        aiClientMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) =>
                new RoutineSuggestionResponseDto(
                    AthleteId: req.AthleteId,
                    ProposedTitle: "Rutina Fuerza",
                    JustificationSummary: "Progresión de cargas",
                    Exercises: [],
                    ProviderUsed: "Ollama"
                ));

        var orchestrator = new ResilientAiService(
            providers: [aiClientMock.Object],
            semanticValidator: null,
            responseCache: _cacheService,
            currentUser: null,
            auditService: auditMock.Object,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(Guid.NewGuid(), "Fuerza tren superior", ["Brazos"]);

        // Act 1: Primer pedido (Cache Miss -> Proveedor original)
        var res1 = await orchestrator.SuggestRoutineAsync(request);

        // Act 2: Segundo pedido (Cache Hit -> Debe indicar sufijo (cache) y loguear en auditoría)
        var res2 = await orchestrator.SuggestRoutineAsync(request);

        // Assert
        Assert.Equal("Ollama", res1.ProviderUsed);
        Assert.Equal("Ollama (cache)", res2.ProviderUsed);

        // Verificamos que se haya invocado la auditoría de IA marcando CacheHit
        auditMock.Verify(a => a.LogAsync(
            It.Is<AiLog>(l => l.Provider == "Ollama (cache)" && l.ValidationResult == "CacheHit"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
