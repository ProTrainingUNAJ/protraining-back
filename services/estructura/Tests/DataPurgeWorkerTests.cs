using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using API.BackgroundServices;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.PurgePendingAthletes;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Estructura.Tests;

public sealed class DataPurgeWorkerTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private readonly Mock<IPendingPurgeRepository> _mockPendingPurgeRepo = new();
    private readonly Mock<IAthletePurgeDispatcher> _mockPurgeDispatcher = new();
    private readonly Mock<IEstructuraPurgeRepository> _mockEstructuraPurgeRepo = new();
    private readonly Mock<IUnitOfWork> _mockUnitOfWork = new();
    private readonly FixedTimeProvider _fixedTimeProvider = new(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));

    private ProcessPendingPurgesCommandHandler CreateHandler()
    {
        return new ProcessPendingPurgesCommandHandler(
            _mockPendingPurgeRepo.Object,
            _mockPurgeDispatcher.Object,
            _mockEstructuraPurgeRepo.Object,
            _mockUnitOfWork.Object,
            _fixedTimeProvider);
    }

    private static PendingPurge CreatePendingPurge(Guid athleteId, DateTime createdAt, Guid? accountId = null)
    {
        var purge = PendingPurge.Create(athleteId, accountId, "Athlete");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))?.SetValue(purge, createdAt);
        return purge;
    }

    [Fact]
    public async Task Given_AthleteSoftDeletedBeyondRetentionPeriod_When_PurgeJobRuns_Then_PurgesDataAcrossAll5ServicesAndMarksAsProcessed()
    {
        // Arrange (Criterio 1: Atleta borrado hace más del plazo definido -> datos desaparecen de los 5 servicios)
        var athleteId = Guid.NewGuid();
        // Borrado hace 45 días (> 30 días de retención por defecto)
        var purge = CreatePendingPurge(athleteId, _fixedTimeProvider.GetUtcNow().UtcDateTime.AddDays(-45));

        _mockPendingPurgeRepo
            .Setup(r => r.ListPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPurge> { purge });

        // Despachador a los 4 servicios externos responde OK
        _mockPurgeDispatcher
            .Setup(d => d.DispatchPurgeToExternalServicesAsync(athleteId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthletePurgeDispatchResult(true));

        var handler = CreateHandler();

        // Act
        var result = await handler.HandleAsync(new ProcessPendingPurgesCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalEvaluated);
        Assert.Equal(1, result.PurgedSuccessfully);
        Assert.Equal(0, result.FailedOrPostponed);
        Assert.Equal(0, result.SkippedWithinRetention);

        // Verificamos que se llamó a los servicios externos (Identidad, Entrenamiento, Seguimiento, Asistente)
        _mockPurgeDispatcher.Verify(d => d.DispatchPurgeToExternalServicesAsync(athleteId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verificamos que se purgó localmente en Estructura (5to servicio)
        _mockEstructuraPurgeRepo.Verify(e => e.PurgeAthleteLocalDataAsync(athleteId, It.IsAny<CancellationToken>()), Times.Once);

        // Verificamos que quedó marcado como Processed y se guardó en BD
        Assert.Equal(PendingPurge.ProcessedStatus, purge.Status);
        Assert.NotNull(purge.ProcessedAt);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_AthleteSoftDeletedWithinRetentionPeriod_When_PurgeJobRuns_Then_SkipsPurgeAndLeavesRecordPending()
    {
        // Arrange (Criterio 1: Atleta borrado dentro del plazo definido -> no se purga todavía)
        var athleteId = Guid.NewGuid();
        // Borrado hace solo 5 días (< 30 días de retención)
        var purge = CreatePendingPurge(athleteId, _fixedTimeProvider.GetUtcNow().UtcDateTime.AddDays(-5));

        _mockPendingPurgeRepo
            .Setup(r => r.ListPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPurge> { purge });

        var handler = CreateHandler();

        // Act
        var result = await handler.HandleAsync(new ProcessPendingPurgesCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalEvaluated);
        Assert.Equal(0, result.PurgedSuccessfully);
        Assert.Equal(0, result.FailedOrPostponed);
        Assert.Equal(1, result.SkippedWithinRetention);

        // No se debe llamar a ningún servicio ni purgar localmente
        _mockPurgeDispatcher.Verify(d => d.DispatchPurgeToExternalServicesAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEstructuraPurgeRepo.Verify(e => e.PurgeAthleteLocalDataAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        // Debe mantenerse en estado Pending
        Assert.Equal(PendingPurge.PendingStatus, purge.Status);
        Assert.Null(purge.ProcessedAt);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_ExternalServiceFailsOrIsDown_When_PurgeJobRuns_Then_LogsWarningDoesNotMarkAsProcessedAndRetriesLaterWithoutLosingOrder()
    {
        // Arrange (Criterio 2: Servicio caído -> reintenta más tarde sin perder el pedido)
        var athleteId = Guid.NewGuid();
        var purge = CreatePendingPurge(athleteId, _fixedTimeProvider.GetUtcNow().UtcDateTime.AddDays(-35));

        _mockPendingPurgeRepo
            .Setup(r => r.ListPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPurge> { purge });

        // Un servicio externo (ej. Seguimiento) falló o está caído
        _mockPurgeDispatcher
            .Setup(d => d.DispatchPurgeToExternalServicesAsync(athleteId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthletePurgeDispatchResult(false, "Error en Seguimiento"));

        var handler = CreateHandler();

        // Act
        var result = await handler.HandleAsync(new ProcessPendingPurgesCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(1, result.TotalEvaluated);
        Assert.Equal(0, result.PurgedSuccessfully);
        Assert.Equal(1, result.FailedOrPostponed);
        Assert.Equal(0, result.SkippedWithinRetention);

        // No se purgan los datos locales de Estructura si falló algún servicio externo
        _mockEstructuraPurgeRepo.Verify(e => e.PurgeAthleteLocalDataAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        // El registro de purga NO debe marcarse como procesado; debe conservarse como Pending y registrar el fallo
        Assert.Equal(PendingPurge.PendingStatus, purge.Status);
        Assert.Null(purge.ProcessedAt);
        Assert.Equal(1, purge.Attempts);
        Assert.Equal("Error en Seguimiento", purge.LastError);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_FailedServiceRecoversOnNextRun_When_PurgeJobRunsAgain_Then_SucceedsWithoutLosingOrder()
    {
        // Arrange (Criterio 2: Reintento posterior exitoso)
        var athleteId = Guid.NewGuid();
        var purge = CreatePendingPurge(athleteId, _fixedTimeProvider.GetUtcNow().UtcDateTime.AddDays(-35));

        _mockPendingPurgeRepo
            .Setup(r => r.ListPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPurge> { purge });

        // En la primera corrida, el servicio está caído
        _mockPurgeDispatcher
            .Setup(d => d.DispatchPurgeToExternalServicesAsync(athleteId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthletePurgeDispatchResult(false, "Servicio temporalmente caído"));

        var handler = CreateHandler();
        var firstRunResult = await handler.HandleAsync(new ProcessPendingPurgesCommand(), CancellationToken.None);

        Assert.Equal(1, firstRunResult.FailedOrPostponed);
        Assert.Equal(PendingPurge.PendingStatus, purge.Status);
        Assert.Equal(1, purge.Attempts);

        // En la segunda corrida, el servicio se recuperó
        _mockPurgeDispatcher
            .Setup(d => d.DispatchPurgeToExternalServicesAsync(athleteId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthletePurgeDispatchResult(true));

        // Act
        var secondRunResult = await handler.HandleAsync(new ProcessPendingPurgesCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(1, secondRunResult.PurgedSuccessfully);
        Assert.Equal(0, secondRunResult.FailedOrPostponed);
        Assert.Equal(PendingPurge.ProcessedStatus, purge.Status);
        Assert.NotNull(purge.ProcessedAt);
        _mockEstructuraPurgeRepo.Verify(e => e.PurgeAthleteLocalDataAsync(athleteId, It.IsAny<CancellationToken>()), Times.Once);
        // Se invoca 2 veces SaveChanges: una en la 1ra corrida para guardar el fallo (attempts/last_error) y otra en la 2da para guardar Processed
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Given_AthletePurgeDispatcher_When_OneServiceThrowsOrFails_Then_RetriesAndReturnsFalseIfStillFailing()
    {
        // Arrange
        var mockClient = new Mock<IExternalServicePurgeClient>();
        var athleteId = Guid.NewGuid();

        // Identidad responde OK
        mockClient.Setup(c => c.PurgeAthleteInServiceAsync("Identidad", It.IsAny<string>(), athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePurgeResult("Identidad", true, 204));

        // Entrenamiento falla ambos intentos
        mockClient.Setup(c => c.PurgeAthleteInServiceAsync("Entrenamiento", It.IsAny<string>(), athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServicePurgeResult("Entrenamiento", false, 503, "Service unavailable"));

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var dispatcher = new AthletePurgeDispatcher(mockClient.Object, config, NullLogger<AthletePurgeDispatcher>.Instance);

        // Act
        var result = await dispatcher.DispatchPurgeToExternalServicesAsync(athleteId, accountId: null, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        // Debe haber intentado 3 veces con retroceso exponencial con Entrenamiento
        mockClient.Verify(c => c.PurgeAthleteInServiceAsync("Entrenamiento", It.IsAny<string>(), athleteId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Given_AthletePurgeDispatcher_When_FirstAttemptFailsAndSecondSucceeds_Then_ReturnsTrue()
    {
        // Arrange
        var mockClient = new Mock<IExternalServicePurgeClient>();
        var athleteId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var callCount = 0;
        mockClient.Setup(c => c.PurgeAthleteInServiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // Primer intento falla, luego se recupera
                if (callCount == 1)
                    return new ServicePurgeResult("Identidad", false, 500, "Temporary error");

                return new ServicePurgeResult("Identidad", true, 200);
            });

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var dispatcher = new AthletePurgeDispatcher(mockClient.Object, config, NullLogger<AthletePurgeDispatcher>.Instance);

        // Act
        var result = await dispatcher.DispatchPurgeToExternalServicesAsync(athleteId, accountId, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Given_DataPurgeWorker_When_ProcessPurgesAsyncInvoked_Then_ResolvesHandlerAndReturnsResult()
    {
        // Arrange
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var handler = CreateHandler();
        _mockPendingPurgeRepo.Setup(r => r.ListPendingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPurge>());

        mockServiceProvider.Setup(p => p.GetService(typeof(ProcessPendingPurgesCommandHandler)))
            .Returns(handler);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Purge:RetentionDays"] = "15"
        }).Build();

        var worker = new DataPurgeWorker(mockScopeFactory.Object, config, NullLogger<DataPurgeWorker>.Instance);

        // Act
        var result = await worker.ProcessPurgesAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalEvaluated);
        mockScopeFactory.Verify(f => f.CreateScope(), Times.Once);
    }
}
