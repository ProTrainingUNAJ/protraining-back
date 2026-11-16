namespace Asistente.Tests;

using Application.DTOs.Ai;
using Application.Interfaces.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Quota;
using Infrastructure.Services.Ai.Rules;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Pruebas unitarias para el limitador de uso de IA por Preparador Físico en memoria (#215 · KAN-510).
/// Criterios de aceptación verificados:
/// 1. Contador por PF y por día con tope configurable.
/// 2. Ante límite alcanzado, desvío automático a reglas determinísticas con aviso claro, sin error 500.
/// 3. Aislamiento estricto entre preparadores físicos (multi-tenancy).
/// 4. Registro seguro y thread-safe.
/// </summary>
public sealed class AiQuotaServiceTests
{
    private readonly IMemoryCache _memoryCache;

    public AiQuotaServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    private AiQuotaService CreateQuotaService(int dailyLimit = 3, bool enabled = true)
    {
        var options = Options.Create(new AiQuotaOptions
        {
            DailyLimitPerPf = dailyLimit,
            Enabled = enabled
        });
        return new AiQuotaService(options, NullLogger<AiQuotaService>.Instance, _memoryCache);
    }

    [Fact]
    public void Given_NewPf_When_CheckingQuota_Then_HasQuotaIsTrue_And_UsageIsZero()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 5);
        var pfId = Guid.NewGuid();

        // Act & Assert
        Assert.True(service.HasQuota(pfId));
        Assert.Equal(0, service.GetCurrentUsage(pfId));
        Assert.Equal(5, service.DailyLimit);
    }

    [Fact]
    public void Given_Pf_When_RecordingUsage_Then_IncrementsUsage_And_TracksAccurately()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 3);
        var pfId = Guid.NewGuid();

        // Act
        var usage1 = service.RecordUsage(pfId);
        var usage2 = service.RecordUsage(pfId);

        // Assert
        Assert.Equal(1, usage1);
        Assert.Equal(2, usage2);
        Assert.Equal(2, service.GetCurrentUsage(pfId));
        Assert.True(service.HasQuota(pfId));
    }

    [Fact]
    public void Given_PfReachedDailyLimit_When_CheckingQuota_Then_HasQuotaReturnsFalse()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 2);
        var pfId = Guid.NewGuid();

        // Act
        service.RecordUsage(pfId);
        service.RecordUsage(pfId);

        // Assert
        Assert.Equal(2, service.GetCurrentUsage(pfId));
        Assert.False(service.HasQuota(pfId));
    }

    [Fact]
    public void Given_TwoDifferentPfs_When_OneReachesLimit_Then_OtherPfQuotaIsNotAffected()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 2);
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();

        // Act
        service.RecordUsage(pfA);
        service.RecordUsage(pfA);

        // Assert: PF A consumió su cuota, pero PF B conserva su cuota intacta
        Assert.False(service.HasQuota(pfA));
        Assert.True(service.HasQuota(pfB));
        Assert.Equal(0, service.GetCurrentUsage(pfB));
    }

    [Fact]
    public void Given_NullOrEmptyPfId_When_CheckingOrRecordingQuota_Then_ReturnsTrueWithoutBlocking()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 1);

        // Act & Assert
        Assert.True(service.HasQuota(null));
        Assert.True(service.HasQuota(Guid.Empty));
        Assert.Equal(0, service.RecordUsage(null));
        Assert.Equal(0, service.RecordUsage(Guid.Empty));
        Assert.Equal(0, service.GetCurrentUsage(null));
    }

    [Fact]
    public void Given_QuotaDisabled_When_PfExceedsDailyLimit_Then_HasQuotaStillReturnsTrue()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 1, enabled: false);
        var pfId = Guid.NewGuid();

        // Act
        service.RecordUsage(pfId);
        service.RecordUsage(pfId);

        // Assert
        Assert.True(service.HasQuota(pfId));
    }

    [Fact]
    public void Given_ConcurrentRequestsFromSamePf_When_RecordingUsage_Then_CountIsThreadSafe()
    {
        // Arrange
        var service = CreateQuotaService(dailyLimit: 100);
        var pfId = Guid.NewGuid();
        const int iterations = 50;

        // Act
        Parallel.For(0, iterations, _ =>
        {
            service.RecordUsage(pfId);
        });

        // Assert
        Assert.Equal(iterations, service.GetCurrentUsage(pfId));
    }

    [Fact]
    public async Task Given_ResilientAiService_When_PfExceedsDailyQuota_Then_FallsBackToRules_WithClearNotice_WithoutCallingExternalAi()
    {
        // Arrange
        var pfId = Guid.NewGuid();
        var quotaService = CreateQuotaService(dailyLimit: 1);
        quotaService.RecordUsage(pfId); // Agotamos la cuota diaria (1/1)

        var externalAiMock = new Mock<IAiClient>();
        externalAiMock.Setup(c => c.ProviderName).Returns("OpenAI");

        var ruleBasedMock = new Mock<IAiClient>();
        ruleBasedMock.Setup(c => c.ProviderName).Returns("Reglas");
        ruleBasedMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) => new RoutineSuggestionResponseDto(
                AthleteId: req.AthleteId,
                ProposedTitle: "Rutina Determinística de Respaldo",
                JustificationSummary: "Propuesta basada en reglas por cuota.",
                Exercises: [new SuggestedExerciseDto("Press de Banca", 3, 10, 60m, Guid.NewGuid())],
                ProviderUsed: "Reglas"
            ));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var auditServiceMock = new Mock<IAiAuditService>();

        var resilientService = new ResilientAiService(
            providers: [externalAiMock.Object, ruleBasedMock.Object],
            semanticValidator: null,
            responseCache: null,
            currentUser: currentUserMock.Object,
            auditService: auditServiceMock.Object,
            catalogService: null,
            quotaService: quotaService,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "Fuerza General"
        );

        // Act
        var response = await resilientService.SuggestRoutineAsync(request);

        // Assert
        // 1. No debe llamar al proveedor externo de IA
        externalAiMock.Verify(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);

        // 2. Debe invocar al proveedor de reglas
        ruleBasedMock.Verify(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);

        // 3. Debe indicar en ProviderUsed que se usaron reglas por límite diario alcanzado
        Assert.Equal("Reglas (Límite diario alcanzado)", response.ProviderUsed);

        // 4. Debe incluir el aviso al preparador físico
        Assert.NotNull(response.AdaptationWarnings);
        Assert.Contains(response.AdaptationWarnings, w => w.Contains("límite de pedidos a la IA por hoy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Given_ResilientAiService_When_PfHasQuota_Then_CallsExternalAi_And_RecordsQuotaUsage()
    {
        // Arrange
        var pfId = Guid.NewGuid();
        var quotaService = CreateQuotaService(dailyLimit: 5);

        var externalAiMock = new Mock<IAiClient>();
        externalAiMock.Setup(c => c.ProviderName).Returns("OpenAI");
        externalAiMock.Setup(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoutineSuggestionRequestDto req, CancellationToken _) => new RoutineSuggestionResponseDto(
                AthleteId: req.AthleteId,
                ProposedTitle: "Rutina con IA",
                JustificationSummary: "Generada con OpenAI",
                Exercises: [new SuggestedExerciseDto("Sentadilla", 4, 8, 80m, Guid.NewGuid())],
                ProviderUsed: "OpenAI"
            ));

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(pfId);

        var resilientService = new ResilientAiService(
            providers: [externalAiMock.Object],
            semanticValidator: null,
            responseCache: null,
            currentUser: currentUserMock.Object,
            auditService: null,
            catalogService: null,
            quotaService: quotaService,
            logger: NullLogger<ResilientAiService>.Instance
        );

        var request = new RoutineSuggestionRequestDto(
            AthleteId: Guid.NewGuid(),
            Goal: "Hipertrofia"
        );

        // Act
        var response = await resilientService.SuggestRoutineAsync(request);

        // Assert
        Assert.Equal("OpenAI", response.ProviderUsed);
        Assert.Equal(1, quotaService.GetCurrentUsage(pfId));
        externalAiMock.Verify(c => c.TrySuggestRoutineAsync(It.IsAny<RoutineSuggestionRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
