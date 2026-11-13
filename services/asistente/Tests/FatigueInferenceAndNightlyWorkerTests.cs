using API.BackgroundServices;
using Application.DTOs.Alerts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Common;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Suite de pruebas automatizadas para la inferencia de fatiga y la ejecución programada
/// del worker nocturno (#181 · KAN-476).
/// Valida el comportamiento del detector de sobrecarga ante proximidad de partidos,
/// deduplicación, aislamiento multitenant y ejecución simulada del BackgroundService.
/// </summary>
public sealed class FatigueInferenceAndNightlyWorkerTests
{
    private readonly Mock<IStructureAthleteClient> _structureMock = new();
    private readonly Mock<ISeguimientoClient> _seguimientoMock = new();
    private readonly Mock<IStructureCalendarClient> _calendarMock = new();
    private readonly Mock<IPreventiveAlertRepository> _alertRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly ProcessDailyOverloadAnalysisCommandHandler _handler;

    private sealed class TestPfDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public FatigueInferenceAndNightlyWorkerTests()
    {
        _currentUserMock
            .Setup(c => c.UsePfContext(It.IsAny<Guid>()))
            .Returns(new TestPfDisposable());

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _handler = new ProcessDailyOverloadAnalysisCommandHandler(
            _structureMock.Object,
            _seguimientoMock.Object,
            _calendarMock.Object,
            _alertRepoMock.Object,
            _unitOfWorkMock.Object,
            _currentUserMock.Object,
            NullLogger<ProcessDailyOverloadAnalysisCommandHandler>.Instance);
    }

    // -------------------------------------------------------------------------
    // FAT-01 — Criterio de Aceptación Principal
    // DADO un test inyectando un patrón de sobreentrenamiento previo a un partido,
    // CUANDO corre el análisis del worker nocturno,
    // ENTONCES se verifica que se genere un aviso con nivel de riesgo "Rojo" y
    // una propuesta de ajuste asociada.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT01_OvertrainingBeforeMatch_GeneratesRedAlertWithAdjustment()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);
        var matchDate = targetDate.AddDays(2); // Partido en 2 días (D-2)

        var athlete = new StructureAthleteSummaryDto(athleteId, "Lautaro Martínez", categoryId, true, pfId);
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        // Patrón de sobreentrenamiento: 3 registros consecutivos con esfuerzo alto (RPE >= 8) y poco sueño (< 5h)
        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate.AddDays(-2), 4.5, 2, 3, 3, 3, 3, 9, false, null, false), // RPE 9, Sueño 4.5h
            new(Guid.NewGuid(), athleteId, targetDate.AddDays(-1), 4.0, 2, 4, 4, 3, 3, 8, false, null, false), // RPE 8, Sueño 4.0h
            new(Guid.NewGuid(), athleteId, targetDate,             4.5, 2, 4, 4, 3, 3, 9, false, null, false)  // RPE 9, Sueño 4.5h
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([matchDate]);

        PreventiveAlert? persistedAlert = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((alert, _) => persistedAlert = alert)
            .Returns(Task.CompletedTask);

        var worker = CreateWorkerInstance();

        // Act: Ejecutamos el análisis nocturno simulado a través del worker real
        var result = await worker.ProcessDailyAnalysisAsync(targetDate, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);

        Assert.NotNull(persistedAlert);
        Assert.Equal(athleteId, persistedAlert.AthleteId);
        Assert.Equal("Lautaro Martínez", persistedAlert.AthleteName);
        Assert.Equal(categoryId, persistedAlert.CategoryId);

        // 1. Nivel de riesgo "Rojo" (AlertSeverity.High)
        Assert.Equal(AlertSeverity.High, persistedAlert.Severity);
        Assert.Equal(AlertType.AcuteOverload, persistedAlert.Type);
        Assert.Contains("Alerta pre-partido", persistedAlert.Reason);
        Assert.Contains("D-2", persistedAlert.Reason);

        // 2. Propuesta de ajuste asociada
        Assert.NotNull(persistedAlert.SuggestedAdjustmentType);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, persistedAlert.SuggestedAdjustmentType);
        Assert.Equal(30, persistedAlert.SuggestedAdjustmentPercentage);
        Assert.Contains("descarga previa al partido", persistedAlert.Suggestion);
        Assert.Contains("Reducir volumen general un 30%", persistedAlert.Suggestion);

        // 3. Estado inicial pendiente y aislamiento multi-tenant
        Assert.Equal(AlertStatus.Pending, persistedAlert.Status);
        Assert.False(persistedAlert.Reviewed);
        _currentUserMock.Verify(c => c.UsePfContext(pfId), Times.Once);
        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // FAT-02 — Comparación Con y Sin Partido Próximo
    // Comprobar que la presencia o ausencia de un partido en el fixture influye
    // en la evaluación de sobrecarga cuando el patrón de mediciones es idéntico.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT02_SameOvertrainingPatternWithoutMatch_AppliesNonMatchRules()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athlete = new StructureAthleteSummaryDto(athleteId, "Julián Álvarez", categoryId, true, pfId);
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        // Mismo historial de esfuerzo reciente (RPE 9) con sueño adecuado (8h) para aislar la regla de partido
        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate, 8.0, 4, 1, 1, 4, 1, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        // ESCENARIO A: Con partido en 2 días (D-2)
        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([targetDate.AddDays(2)]);

        PreventiveAlert? alertWithMatch = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => alertWithMatch = a)
            .Returns(Task.CompletedTask);

        var resultWithMatch = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // ESCENARIO B: Sin partido próximo en el calendario
        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]); // Calendario vacío

        PreventiveAlert? alertWithoutMatch = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => alertWithoutMatch = a)
            .Returns(Task.CompletedTask);

        var resultWithoutMatch = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert: Con partido se activa la alerta pre-partido (D-2)
        Assert.Equal(1, resultWithMatch.GeneratedAlerts);
        Assert.NotNull(alertWithMatch);
        Assert.Contains("Alerta pre-partido (D-2)", alertWithMatch.Reason);
        Assert.Equal(30, alertWithMatch.SuggestedAdjustmentPercentage);

        // Sin partido próximo y con solo 1 sesión de esfuerzo alto (con sueño normal), NO se activa la regla pre-partido
        Assert.Equal(0, resultWithoutMatch.GeneratedAlerts);
        Assert.Null(alertWithoutMatch);
    }

    // -------------------------------------------------------------------------
    // FAT-03 — Valores Límite
    // Verificar operadores inclusivos y exclusivos en esfuerzo (RPE 7 vs 8)
    // y proximidad de partido (D-2 vs D-3).
    // -------------------------------------------------------------------------
    [Fact]
    public void FAT03_ThresholdBoundaryValues_RespectsInclusiveAndExclusiveLimits()
    {
        var athleteId = Guid.NewGuid();
        var matchDate = new DateOnly(2026, 11, 6);

        // 1. Límite de Esfuerzo con partido en D-2:
        // RPE 7 (justo por debajo del umbral >= 8) -> No debe activar alerta pre-partido
        var recordRpe7 = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 4), 8.0, 4, 1, 1, 4, 1, 7, false, null, false)
        };
        var resultRpe7 = OverloadPatternDetector.Detect(
            athleteId,
            recordRpe7,
            hasMatchInTwoDays: true,
            matchDate: matchDate,
            daysUntilMatch: 2);
        Assert.False(resultRpe7.HasPattern);

        // RPE 8 (límite inclusivo exacto >= 8) -> DEBE activar alerta pre-partido High
        var recordRpe8 = new List<AthleteCheckInRecord>
        {
            new(new DateOnly(2026, 11, 4), 8.0, 4, 1, 1, 4, 1, 8, false, null, false)
        };
        var resultRpe8 = OverloadPatternDetector.Detect(
            athleteId,
            recordRpe8,
            hasMatchInTwoDays: true,
            matchDate: matchDate,
            daysUntilMatch: 2);
        Assert.True(resultRpe8.HasPattern);
        Assert.Equal(AlertSeverity.High, resultRpe8.Severity);
        Assert.Equal(AlertType.AcuteOverload, resultRpe8.Type);

        // 2. Límite de Proximidad de Partido:
        // daysUntilMatch = 2 (D-2) -> Activa alerta
        var resultD2 = OverloadPatternDetector.Detect(
            athleteId,
            recordRpe8,
            hasMatchInTwoDays: true,
            matchDate: matchDate,
            daysUntilMatch: 2);
        Assert.True(resultD2.HasPattern);

        // daysUntilMatch = 3 (D-3, fuera de ventana 1 o 2 días) -> NO activa alerta pre-partido
        var resultD3 = OverloadPatternDetector.Detect(
            athleteId,
            recordRpe8,
            hasMatchInTwoDays: false,
            matchDate: matchDate.AddDays(1),
            daysUntilMatch: 3);
        Assert.False(resultD3.HasPattern);
    }

    // -------------------------------------------------------------------------
    // FAT-04 — Datos Insuficientes
    // Comprobar que atletas sin check-ins o con registros incompletos
    // no generan conclusiones ficticias ni avisos falsos.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT04_InsufficientOrEmptyData_DoesNotGenerateAlerts()
    {
        // Arrange
        var athlete1 = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta Sin Datos", null, true, Guid.NewGuid());
        var athlete2 = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta Datos Moderados", null, true, Guid.NewGuid());

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete1, athlete2]);

        // Atleta 1 no tiene check-ins; Atleta 2 tiene 1 solo check-in de esfuerzo leve y descanso pleno
        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athlete2.Id, new DateOnly(2026, 11, 4), 8.5, 5, 1, 1, 5, 1, 4, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4)));

        // Assert
        Assert.Equal(2, result.ProcessedAthletes);
        Assert.Equal(0, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // FAT-05 — Procesamiento de Varios Atletas del Mismo PF
    // Lote con múltiples atletas: solo el atleta en riesgo debe recibir aviso.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT05_MultipleAthletesSamePf_OnlyAlertsAthletesAtRisk()
    {
        // Arrange
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athleteAtRisk = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta En Riesgo", categoryId, true, pfId);
        var athleteNormal = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta Normal", categoryId, true, pfId);
        var athleteNoData = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta Sin Datos", categoryId, true, pfId);

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athleteAtRisk, athleteNormal, athleteNoData]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            // Atleta en riesgo: RPE 9 previo a partido en D-2
            new(Guid.NewGuid(), athleteAtRisk.Id, targetDate, 8.0, 4, 1, 1, 4, 1, 9, false, null, false),
            // Atleta normal: RPE 5, sueño 8h
            new(Guid.NewGuid(), athleteNormal.Id, targetDate, 8.0, 4, 1, 1, 4, 1, 5, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([targetDate.AddDays(2)]);

        PreventiveAlert? savedAlert = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => savedAlert = a)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert
        Assert.Equal(3, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        Assert.NotNull(savedAlert);
        Assert.Equal(athleteAtRisk.Id, savedAlert.AthleteId);
        Assert.Equal("Atleta En Riesgo", savedAlert.AthleteName);
    }

    // -------------------------------------------------------------------------
    // FAT-06 — Aislamiento Multi-Tenant
    // Atletas de diferentes PF deben procesarse en ámbitos independientes
    // con su respectivo PfId sin contaminaciones cruzadas.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT06_MultiTenantIsolation_MaintainsStrictPfSeparation()
    {
        // Arrange
        var pfAId = Guid.NewGuid();
        var pfBId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athleteA = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta de PF A", null, true, pfAId);
        var athleteB = new StructureAthleteSummaryDto(Guid.NewGuid(), "Atleta de PF B", null, true, pfBId);

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athleteA, athleteB]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            // Ambos atletas con esfuerzo alto (RPE 9) y déficit de sueño (5h)
            new(Guid.NewGuid(), athleteA.Id, targetDate, 5.0, 2, 4, 3, 3, 3, 9, false, null, false),
            new(Guid.NewGuid(), athleteB.Id, targetDate, 5.0, 2, 4, 3, 3, 3, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        var savedAlerts = new List<PreventiveAlert>();
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => savedAlerts.Add(a))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert
        Assert.Equal(2, result.ProcessedAthletes);
        Assert.Equal(2, result.GeneratedAlerts);
        Assert.Equal(2, savedAlerts.Count);

        // Verifica que UsePfContext fue invocado para cada PF
        _currentUserMock.Verify(c => c.UsePfContext(pfAId), Times.Once);
        _currentUserMock.Verify(c => c.UsePfContext(pfBId), Times.Once);

        // Verifica que cada alerta pertenece al atleta correcto
        Assert.Contains(savedAlerts, a => a.AthleteId == athleteA.Id && a.AthleteName == "Atleta de PF A");
        Assert.Contains(savedAlerts, a => a.AthleteId == athleteB.Id && a.AthleteName == "Atleta de PF B");
    }

    // -------------------------------------------------------------------------
    // FAT-07 — Simulación de Ejecución Programada del Worker
    // Invocar el procesamiento del BackgroundService de manera controlada
    // mediante ProcessDailyAnalysisAsync sin esperar a las 03:00 AM.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT07_SimulatedNightlyWorkerRun_ExecutesAnalysisDeterministically()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athlete = new StructureAthleteSummaryDto(athleteId, "Emiliano Martínez", categoryId, true, pfId);
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate, 8.0, 4, 1, 1, 4, 1, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([targetDate.AddDays(1)]); // Partido mañana (D-1)

        var worker = CreateWorkerInstance();

        // Act
        var result = await worker.ProcessDailyAnalysisAsync(targetDate, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.ProcessedAthletes);
        Assert.Equal(1, result.GeneratedAlerts);
        Assert.Equal(0, result.SkippedAlerts);
    }

    // -------------------------------------------------------------------------
    // FAT-08 — Ejecuciones Repetidas (Idempotencia)
    // Ejecutar dos veces el análisis sobre la misma ventana no debe duplicar alertas.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT08_RepeatedExecution_DoesNotDuplicatePendingAlerts()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athlete = new StructureAthleteSummaryDto(athleteId, "Cristian Romero", categoryId, true, pfId);
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate, 8.0, 4, 1, 1, 4, 1, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([targetDate.AddDays(2)]);

        PreventiveAlert? savedAlert = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => savedAlert = a)
            .Returns(Task.CompletedTask);

        // 1. Primera ejecución: genera la alerta
        var firstResult = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));
        Assert.Equal(1, firstResult.GeneratedAlerts);
        Assert.Equal(0, firstResult.SkippedAlerts);
        Assert.NotNull(savedAlert);

        // Simular que la alerta ahora ya se encuentra en el repositorio como pendiente
        _alertRepoMock
            .Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([savedAlert]);

        // 2. Segunda ejecución sobre la misma ventana: debe omitir por duplicado
        var secondResult = await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert: Idempotencia comprobada
        Assert.Equal(0, secondResult.GeneratedAlerts);
        Assert.Equal(1, secondResult.SkippedAlerts);
        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // FAT-09 — Manejo de Errores y Fallo de Dependencia
    // Simulación controlada de indisponibilidad de servicio externo:
    // no se generan avisos corruptos y la persistencia no se ejecuta.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT09_DependencyFailure_LogsAndHandlesGracefullyWithoutCorruptingState()
    {
        // Arrange: Estructura no responde
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureServiceUnavailableException("Servicio de Estructura no disponible."));

        // Act & Assert
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 4))));

        // Verificamos que no se persistió ninguna alerta corrupta
        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // FAT-10 — Cancelación con CancellationToken
    // Comprobar que el worker y el caso de uso respetan el token de cancelación.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT10_CancellationToken_AbortsProcessingCleanly()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Token cancelado inmediatamente

        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var worker = CreateWorkerInstance();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.ProcessDailyAnalysisAsync(new DateOnly(2026, 11, 4), cts.Token));

        _alertRepoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // FAT-11 — Asociación de Propuesta de Ajuste
    // Verificar que el aviso Rojo generado contiene una propuesta de ajuste
    // válida con tipo concreto, porcentaje y recomendaciones accionables.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task FAT11_RedAlertAdjustmentProposal_ContainsValidStructuredData()
    {
        // Arrange
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var targetDate = new DateOnly(2026, 11, 4);

        var athlete = new StructureAthleteSummaryDto(athleteId, "Rodrigo De Paul", categoryId, true, pfId);
        _structureMock
            .Setup(s => s.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([athlete]);

        var checkIns = new List<AthleteCheckInHistoryItemDto>
        {
            new(Guid.NewGuid(), athleteId, targetDate, 8.0, 4, 1, 1, 4, 1, 9, false, null, false)
        };

        _seguimientoMock
            .Setup(s => s.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns);

        _calendarMock
            .Setup(c => c.GetMatchDatesAsync(categoryId, targetDate.AddDays(1), targetDate.AddDays(2), It.IsAny<CancellationToken>()))
            .ReturnsAsync([targetDate.AddDays(2)]);

        PreventiveAlert? savedAlert = null;
        _alertRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Callback<PreventiveAlert, CancellationToken>((a, _) => savedAlert = a)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(targetDate));

        // Assert: Verificación de la estructura contractual de la propuesta
        Assert.NotNull(savedAlert);
        Assert.Equal(AlertSeverity.High, savedAlert.Severity);
        Assert.Equal(AlertType.AcuteOverload, savedAlert.Type);
        Assert.Equal(AlertStatus.Pending, savedAlert.Status);

        // Propuesta tipada
        Assert.NotNull(savedAlert.SuggestedAdjustmentType);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, savedAlert.SuggestedAdjustmentType);

        // Porcentaje válido entre 1 y 100
        Assert.NotNull(savedAlert.SuggestedAdjustmentPercentage);
        Assert.InRange(savedAlert.SuggestedAdjustmentPercentage.Value, 1, 100);
        Assert.Equal(30, savedAlert.SuggestedAdjustmentPercentage.Value);

        // Sugerencia textual explícita para la toma de decisión del PF
        Assert.False(string.IsNullOrWhiteSpace(savedAlert.Suggestion));
        Assert.Contains("activación neuromuscular suave", savedAlert.Suggestion);
        Assert.Contains("descarga previa al partido", savedAlert.Suggestion);
    }

    // -------------------------------------------------------------------------
    // Helper: Instanciación del worker con ServiceScopeFactory
    // -------------------------------------------------------------------------
    private DailyOverloadAnalysisWorker CreateWorkerInstance()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _handler);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder().Build();

        return new DailyOverloadAnalysisWorker(
            scopeFactory,
            config,
            NullLogger<DailyOverloadAnalysisWorker>.Instance);
    }
}

