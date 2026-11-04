using API.BackgroundServices;
using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas automatizadas para el worker nocturno y el caso de uso de análisis de sobrecarga (KAN-472 · #177).
/// </summary>
public sealed class DailyOverloadAnalysisWorkerTests
{
    private readonly Mock<IStructureAthleteClient> _structureMock = new();
    private readonly Mock<ISeguimientoClient> _seguimientoMock = new();
    private readonly Mock<IPreventiveAlertRepository> _alertRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly ProcessDailyOverloadAnalysisCommandHandler _handler;

    public DailyOverloadAnalysisWorkerTests()
    {
        _handler = new ProcessDailyOverloadAnalysisCommandHandler(
            _structureMock.Object,
            _seguimientoMock.Object,
            _alertRepoMock.Object,
            _unitOfWorkMock.Object,
            NullLogger<ProcessDailyOverloadAnalysisCommandHandler>.Instance);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_SobrecargaDetectada_GeneraAlertaYPersisteEnDb()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Lucas Fernández", categoryId);

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 2), 5.0, 2, 4, 3, 3, 3, 9, false, null, false), // RPE 9 + Sueño 5h
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 3), 7.0, 3, 2, 2, 4, 2, 6, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        PreventiveAlert? savedAlert = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => savedAlert = a)
            .Returns(Task.CompletedTask);

        // Act
        var command = new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4));
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);

        Assert.NotNull(savedAlert);
        Assert.Equal(athleteId, savedAlert.AthleteId);
        Assert.Equal("Lucas Fernández", savedAlert.AthleteName);
        Assert.Equal(AlertSeverity.High, savedAlert.Severity);
        Assert.Equal(AlertType.AccumulatedFatigue, savedAlert.Type);
        Assert.Equal(AlertStatus.Pending, savedAlert.Status);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, savedAlert.SuggestedAdjustmentType);
        Assert.Equal(25, savedAlert.SuggestedAdjustmentPercentage);

        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_AlertaYaPendienteDelMismoTipo_OmiteDuplicado()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Martín Rossi");

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 3), 5.0, 2, 4, 3, 3, 3, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        // Ya existe una alerta pendiente para este atleta y este tipo (AccumulatedFatigue)
        var existingAlert = PreventiveAlert.Create(
            athleteId,
            "Martín Rossi",
            "Fatiga previa",
            "Descarga",
            AlertSeverity.High,
            AlertType.AccumulatedFatigue);

        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingAlert]);

        // Act
        var command = new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4));
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(1, result.SkippedAlerts);

        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_AtletasSaludables_CeroAlertasGeneradas()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Tomás Pérez");

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var healthyCheckIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 2), 8.0, 4, 1, 1, 4, 1, 6, false, null, false),
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 3), 8.5, 5, 1, 1, 5, 1, 5, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(healthyCheckIns);

        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(), CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_SinAtletasEnEstructura_TerminaLimpio()
    {
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(), CancellationToken.None);

        Assert.Equal(0, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
        _seguimientoMock.Verify(s => s.GetAthleteCheckInsHistoryAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_SeguimientoVacioOCaido_NoLanzaExcepcion()
    {
        var athlete = new StructureAthleteSummaryDto(Guid.NewGuid(), "Rodrigo Díaz");
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(), CancellationToken.None);

        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
    }

    [Fact]
    public async Task DailyOverloadAnalysisWorker_ProcessDailyAnalysisAsync_InvocaCasoDeUsoDentroDeScope()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Lucas Fernández");

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 2), 5.0, 2, 4, 3, 3, 3, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => _handler);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder().Build();
        var worker = new DailyOverloadAnalysisWorker(
            scopeFactory,
            config,
            NullLogger<DailyOverloadAnalysisWorker>.Instance);

        // Act
        var result = await worker.ProcessDailyAnalysisAsync(new DateOnly(2026, 11, 4), CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
    }
}
