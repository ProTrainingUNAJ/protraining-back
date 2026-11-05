using API.BackgroundServices;
using API.Services;
using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas automatizadas para el worker nocturno y el caso de uso de análisis de sobrecarga y fixture (KAN-472 · #177 / KAN-473 · #178).
/// </summary>
public sealed class DailyOverloadAnalysisWorkerTests
{
    private readonly Mock<IStructureAthleteClient> _structureMock = new();
    private readonly Mock<ISeguimientoClient> _seguimientoMock = new();
    private readonly Mock<IStructureCalendarClient> _calendarMock = new();
    private readonly Mock<IPreventiveAlertRepository> _alertRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly ProcessDailyOverloadAnalysisCommandHandler _handler;

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public DailyOverloadAnalysisWorkerTests()
    {
        _currentUserMock
            .Setup(c => c.UsePfContext(It.IsAny<Guid>()))
            .Returns(new EmptyDisposable());

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _handler = new ProcessDailyOverloadAnalysisCommandHandler(
            _structureMock.Object,
            _seguimientoMock.Object,
            _calendarMock.Object,
            _alertRepoMock.Object,
            _unitOfWorkMock.Object,
            _currentUserMock.Object,
            NullLogger<ProcessDailyOverloadAnalysisCommandHandler>.Instance);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_SobrecargaDetectada_GeneraAlertaYPersisteEnDb()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Lucas Fernández", categoryId, true, pfId);

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
        _currentUserMock.Verify(c => c.UsePfContext(pfId), Times.Once);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_AgrupaPorPf_E_InvocaUsePfContext_ParaCadaPreparadorFisico()
    {
        // Arrange: 2 atletas de PFs distintos
        var pf1 = Guid.NewGuid();
        var pf2 = Guid.NewGuid();
        var athlete1 = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta 1", null, true, pf1);
        var athlete2 = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta 2", null, true, pf2);

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete1, athlete2]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athlete1.Id, new DateOnly(2026, 11, 2), 5.0, 2, 4, 3, 3, 3, 9, false, null, false),
            new(Guid.NewGuid(), athlete2.Id, new DateOnly(2026, 11, 2), 5.0, 2, 4, 3, 3, 3, 9, false, null, false)
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

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4)));

        // Assert
        Assert.Equal(2, result.ProcessedAthletes);
        Assert.Equal(2, result.GeneratedAlerts);

        // Verifica que el handler establece el contexto efímero de cada PF exactamente una vez
        _currentUserMock.Verify(c => c.UsePfContext(pf1), Times.Once);
        _currentUserMock.Verify(c => c.UsePfContext(pf2), Times.Once);
    }

    [Fact]
    public void CurrentUser_UsePfContext_EstableceAmbientPfId_Y_RevierteAlDisposar()
    {
        // Arrange
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        httpContextAccessorMock.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var currentUser = new CurrentUser(httpContextAccessorMock.Object);

        // Assert inicial: sin token y sin ambient, PfId es null
        Assert.Null(currentUser.PfId);

        var pfId = Guid.NewGuid();

        // Act & Assert dentro del scope: PfId devuelve el pfId del contexto efímero
        using (currentUser.UsePfContext(pfId))
        {
            Assert.Equal(pfId, currentUser.PfId);
        }

        // Assert posterior: al disponer, revierte a null
        Assert.Null(currentUser.PfId);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_AlertaYaPendiente_OmiteDuplicado()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Nicolás González", categoryId, true, pfId);

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

        // Ya existe una alerta pendiente de tipo AccumulatedFatigue
        var existingAlert = PreventiveAlert.Create(
            athleteId,
            "Nicolás González",
            "Fatiga previa",
            "Sugerencia previa",
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
    public async Task ProcessDailyOverloadAnalysis_SinPatronSobrecarga_NoGeneraAlerta()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Martín Rossi", null, true, pfId);

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 2), 8.0, 4, 1, 1, 4, 1, 5, false, null, false), // RPE 5, sueño 8h
            new(Guid.NewGuid(), athleteId, new DateOnly(2026, 11, 3), 8.0, 4, 1, 1, 4, 1, 4, false, null, false)
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

        // Act
        var command = new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4));
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);

        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_ConPartidoEnD2_GeneraAlertaAcuteOverloadPrePartido()
    {
        // Arrange: Atleta con esfuerzo alto reciente (RPE 9) y categoría con partido en 2 días (#178 · KAN-473)
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);
        var matchDate = targetDate.AddDays(2); // Partido en D-2

        var athlete = new StructureAthleteSummaryDto(athleteId, "Tomás Pérez", categoryId, true, pfId);

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate, 8.0, 4, 1, 1, 4, 1, 9, false, null, false) // RPE 9
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate, matchDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchDate]);

        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        PreventiveAlert? savedAlert = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => savedAlert = a)
            .Returns(Task.CompletedTask);

        // Act
        var command = new ProcessDailyOverloadAnalysisCommand(targetDate);
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert: Criterio KAN-473 cumplido
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        Assert.NotNull(savedAlert);
        Assert.Equal(AlertType.AcuteOverload, savedAlert.Type);
        Assert.Equal(AlertSeverity.High, savedAlert.Severity);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, savedAlert.SuggestedAdjustmentType);
        Assert.Equal(30, savedAlert.SuggestedAdjustmentPercentage);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_SinAtletasActivos_RetornaCeroProcesados()
    {
        // Arrange
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4)));

        // Assert
        Assert.Equal(0, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
    }

    [Fact]
    public async Task ProcessDailyOverloadAnalysis_SinCheckIns_RetornaCeroAlertas()
    {
        // Arrange
        var athlete = new StructureAthleteSummaryDto(Guid.NewGuid(), "Rodrigo Díaz", null, true, Guid.NewGuid());
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

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4)));

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
    }

    [Fact]
    public async Task DailyOverloadAnalysisWorker_ProcessDailyAnalysisAsync_InvocaCasoDeUsoDentroDeScope()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var athlete = new StructureAthleteSummaryDto(athleteId, "Lucas Fernández", null, true, pfId);

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
