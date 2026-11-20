using System.Reflection;
using API.Controllers;
using Application.Common.Alerts;
using Application.DTOs.Alerts;
using Application.DTOs.DayAdjustments;
using Application.DTOs.DayConflicts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.ApplyAlertAdjustment;
using Application.UseCases.Alerts.CreatePreventiveAlert;
using Application.UseCases.Alerts.GetAlertById;
using Application.UseCases.Alerts.GetAlerts;
using Application.UseCases.Alerts.ReviewAlert;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas automatizadas para el Centro de Avisos Preventivos con propuestas de ajuste de carga (KAN-474 · #179).
/// </summary>
public sealed class PreventiveAlertsTests
{
    private static readonly Guid PfId = Guid.NewGuid();
    private static readonly Guid AthleteId = Guid.NewGuid();
    private const string AthleteName = "Lucas Fernández";
    private const string ReasonFatigue = "Sobrecarga acumulada: 3 sesiones consecutivas con esfuerzo >= 8 y registro de descanso menor a 5 horas.";
    private const string SuggestionAdjustment = "Programar una sesión de descarga o movilidad pasiva. Reducir volumen general un 25%.";

    [Fact]
    public void Given_ValidData_When_CreateAlert_Then_SetsPropertiesAndStatusIsPending()
    {
        var alert = PreventiveAlert.Create(
            athleteId: AthleteId,
            athleteName: AthleteName,
            reason: ReasonFatigue,
            suggestion: SuggestionAdjustment,
            severity: AlertSeverity.High,
            type: AlertType.AccumulatedFatigue,
            suggestedAdjustmentType: SuggestedAdjustmentType.LoadReduction,
            suggestedAdjustmentPercentage: 25);

        Assert.NotEqual(Guid.Empty, alert.Id);
        Assert.Equal(AthleteId, alert.AthleteId);
        Assert.Equal(AthleteName, alert.AthleteName);
        Assert.Equal(AlertSeverity.High, alert.Severity);
        Assert.Equal(AlertType.AccumulatedFatigue, alert.Type);
        Assert.Equal(ReasonFatigue, alert.Reason);
        Assert.Equal(SuggestionAdjustment, alert.Suggestion);
        Assert.Equal(SuggestedAdjustmentType.LoadReduction, alert.SuggestedAdjustmentType);
        Assert.Equal(25, alert.SuggestedAdjustmentPercentage);
        Assert.Equal(AlertStatus.Pending, alert.Status);
        Assert.False(alert.Reviewed);
        Assert.Null(alert.ReviewedAtUtc);
    }

    [Fact]
    public void Given_EmptyAthleteId_When_CreateAlert_Then_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreventiveAlert.Create(Guid.Empty, AthleteName, ReasonFatigue, SuggestionAdjustment));

        Assert.Contains("identificador del atleta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Given_InvalidAthleteName_When_CreateAlert_Then_ThrowsDomainException(string? invalidName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreventiveAlert.Create(AthleteId, invalidName!, ReasonFatigue, SuggestionAdjustment));

        Assert.Contains("nombre del atleta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Given_InvalidReason_When_CreateAlert_Then_ThrowsDomainException(string? invalidReason)
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreventiveAlert.Create(AthleteId, AthleteName, invalidReason!, SuggestionAdjustment));

        Assert.Contains("motivo o causa", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Given_InvalidSuggestion_When_CreateAlert_Then_ThrowsDomainException(string? invalidSuggestion)
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, invalidSuggestion!));

        Assert.Contains("sugerencia concreta", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(101)]
    public void Given_InvalidPercentage_When_CreateAlert_Then_ThrowsDomainException(int invalidPercentage)
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment,
                suggestedAdjustmentPercentage: invalidPercentage));

        Assert.Contains("porcentaje de ajuste", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Given_PendingAlert_When_MarkAsReviewed_Then_TransitionsToReviewed()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        var now = DateTime.UtcNow;

        alert.MarkAsReviewed(now);

        Assert.Equal(AlertStatus.Reviewed, alert.Status);
        Assert.True(alert.Reviewed);
        Assert.Equal(now, alert.ReviewedAtUtc);
    }

    [Fact]
    public void Given_PendingAlert_When_MarkAsApplied_Then_TransitionsToAppliedAndReviewed()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        var now = DateTime.UtcNow;

        alert.MarkAsApplied(now);

        Assert.Equal(AlertStatus.Applied, alert.Status);
        Assert.True(alert.Reviewed);
        Assert.Equal(now, alert.AppliedAtUtc);
        Assert.Equal(now, alert.ReviewedAtUtc);
    }

    [Fact]
    public void Given_PendingAlert_When_Dismiss_Then_TransitionsToDismissed()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        var now = DateTime.UtcNow;

        alert.Dismiss(now);

        Assert.Equal(AlertStatus.Dismissed, alert.Status);
        Assert.True(alert.Reviewed);
        Assert.Equal(now, alert.ReviewedAtUtc);
    }


    [Fact]
    public async Task Given_GetAlertsQuery_When_OnlyUnreviewed_Then_ReturnsPendingAlerts()
    {
        var alert1 = PreventiveAlert.Create(AthleteId, "Atleta 1", ReasonFatigue, SuggestionAdjustment);
        var alert2 = PreventiveAlert.Create(AthleteId, "Atleta 2", ReasonFatigue, SuggestionAdjustment);
        alert2.MarkAsReviewed();

        var repositoryMock = new Mock<IPreventiveAlertRepository>();
        repositoryMock.Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([alert1]);

        var handler = new GetAlertsQueryHandler(repositoryMock.Object);
        var result = await handler.HandleAsync(new GetAlertsQuery(OnlyUnreviewed: true));

        Assert.Single(result);
        Assert.Equal(alert1.Id, result[0].Id);
        Assert.False(result[0].Reviewed);
    }

    [Fact]
    public async Task Given_GetAlertsQuery_When_SeverityFilterProvided_Then_PassesSeverityToRepository()
    {
        var alertHigh = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment, severity: AlertSeverity.High);

        var repositoryMock = new Mock<IPreventiveAlertRepository>();
        repositoryMock.Setup(r => r.ListAsync(false, AlertSeverity.High, It.IsAny<CancellationToken>()))
            .ReturnsAsync([alertHigh]);

        var handler = new GetAlertsQueryHandler(repositoryMock.Object);
        var result = await handler.HandleAsync(new GetAlertsQuery(OnlyUnreviewed: false, Severity: "High"));

        Assert.Single(result);
        Assert.Equal("High", result[0].Severity);
    }

    [Fact]
    public async Task Given_GetAlertById_When_Found_Then_ReturnsMappedDto()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment, severity: AlertSeverity.High);

        var repositoryMock = new Mock<IPreventiveAlertRepository>();
        repositoryMock.Setup(r => r.GetByIdAsNoTrackingAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var handler = new GetAlertByIdQueryHandler(repositoryMock.Object);
        var result = await handler.HandleAsync(new GetAlertByIdQuery(alert.Id));

        Assert.NotNull(result);
        Assert.Equal(alert.Id, result.Id);
        Assert.Equal(AthleteName, result.AthleteName);
        Assert.Equal(ReasonFatigue, result.Reason);
        Assert.Equal(SuggestionAdjustment, result.Suggestion);
    }

    [Fact]
    public async Task Given_GetAlertById_When_NotFound_Then_ThrowsNotFoundException()
    {
        var nonExistentId = Guid.NewGuid();
        var repositoryMock = new Mock<IPreventiveAlertRepository>();
        repositoryMock.Setup(r => r.GetByIdAsNoTrackingAsync(nonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PreventiveAlert?)null);

        var handler = new GetAlertByIdQueryHandler(repositoryMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetAlertByIdQuery(nonExistentId)));
    }

    [Fact]
    public async Task Given_ReviewAlertCommand_When_Found_Then_MarksReviewedAndSaves()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        var repositoryMock = new Mock<IPreventiveAlertRepository>();
        repositoryMock.Setup(r => r.GetByIdAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ReviewAlertCommandHandler(repositoryMock.Object, unitOfWorkMock.Object);
        var result = await handler.HandleAsync(new ReviewAlertCommand(alert.Id));

        Assert.Equal(alert.Id, result.AlertId);
        Assert.True(alert.Reviewed);
        Assert.Equal(AlertStatus.Reviewed, alert.Status);
        unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ReviewAlertCommand_When_NotFound_Then_ThrowsNotFoundException()
    {
        var alienAlertId = Guid.NewGuid();
        var repositoryMock = new Mock<IPreventiveAlertRepository>();
        repositoryMock.Setup(r => r.GetByIdAsync(alienAlertId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PreventiveAlert?)null);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var handler = new ReviewAlertCommandHandler(repositoryMock.Object, unitOfWorkMock.Object);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new ReviewAlertCommand(alienAlertId)));
    }

    // -------------------------------------------------------------------------
    // 3. APLICACIÓN DE AJUSTE CON 1 CLIC EN ENTRENAMIENTO (#183 · KAN-478)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Given_ApplyAlertAdjustmentCommand_When_ValidAlertAndSessionExists_Then_CallsTrainingServiceAndMarksAlertAsApplied()
    {
        var alert = PreventiveAlert.Create(
            athleteId: AthleteId,
            athleteName: AthleteName,
            reason: ReasonFatigue,
            suggestion: SuggestionAdjustment,
            severity: AlertSeverity.High,
            type: AlertType.AccumulatedFatigue,
            suggestedAdjustmentType: SuggestedAdjustmentType.LoadReduction,
            suggestedAdjustmentPercentage: 20);

        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.GetByIdAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var sessionId = Guid.NewGuid();
        var clientMock = new Mock<ITrainingPlanServiceClient>();
        clientMock.Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(
                AthleteId: AthleteId,
                Date: Domain.Common.ArgentinaTime.Today,
                Exercises: [],
                SessionId: sessionId));

        clientMock.Setup(c => c.ApplyAthleteSessionAdjustmentAsync(
                sessionId,
                It.Is<ApplyAthleteAdjustmentRequestRemoteDto>(req =>
                    req.AthleteId == AthleteId &&
                    req.LoadReductionPercentage == 20),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ApplyAlertAdjustmentCommandHandler(repoMock.Object, clientMock.Object, uowMock.Object);
        var result = await handler.HandleAsync(new ApplyAlertAdjustmentCommand(alert.Id));

        Assert.Equal(alert.Id, result.AlertId);
        Assert.Equal("Applied", result.Status);
        Assert.Equal(AlertStatus.Applied, alert.Status);
        Assert.NotNull(alert.AppliedAtUtc);
        clientMock.Verify(c => c.ApplyAthleteSessionAdjustmentAsync(sessionId, It.IsAny<ApplyAthleteAdjustmentRequestRemoteDto>(), It.IsAny<CancellationToken>()), Times.Once);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Given_ApplyAlertAdjustmentCommand_When_TrainingServiceFails_Then_ThrowsExceptionAndAlertRemainsPending()
    {
        // Regla KAN-373/KAN-478: Si Entrenamiento falla (500 o timeout), el aviso NO debe marcarse como aplicado
        var alert = PreventiveAlert.Create(
            athleteId: AthleteId,
            athleteName: AthleteName,
            reason: ReasonFatigue,
            suggestion: SuggestionAdjustment,
            severity: AlertSeverity.High,
            type: AlertType.AccumulatedFatigue,
            suggestedAdjustmentType: SuggestedAdjustmentType.LoadReduction,
            suggestedAdjustmentPercentage: 20);

        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.GetByIdAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var sessionId = Guid.NewGuid();
        var clientMock = new Mock<ITrainingPlanServiceClient>();
        clientMock.Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(
                AthleteId: AthleteId,
                Date: Domain.Common.ArgentinaTime.Today,
                Exercises: [],
                SessionId: sessionId));

        clientMock.Setup(c => c.ApplyAthleteSessionAdjustmentAsync(
                sessionId,
                It.IsAny<ApplyAthleteAdjustmentRequestRemoteDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Error 500: Servicio de Entrenamiento no disponible temporalmente"));

        var uowMock = new Mock<IUnitOfWork>();

        var handler = new ApplyAlertAdjustmentCommandHandler(repoMock.Object, clientMock.Object, uowMock.Object);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            handler.HandleAsync(new ApplyAlertAdjustmentCommand(alert.Id)));

        Assert.Equal(AlertStatus.Pending, alert.Status);
        Assert.Null(alert.AppliedAtUtc);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_ApplyAlertAdjustmentCommand_When_AlreadyApplied_Then_ReturnsEarlyWithoutCallingTrainingService()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        alert.MarkAsApplied(DateTime.UtcNow);

        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.GetByIdAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var clientMock = new Mock<ITrainingPlanServiceClient>();
        var uowMock = new Mock<IUnitOfWork>();

        var handler = new ApplyAlertAdjustmentCommandHandler(repoMock.Object, clientMock.Object, uowMock.Object);
        var result = await handler.HandleAsync(new ApplyAlertAdjustmentCommand(alert.Id));

        Assert.Equal("El aviso ya se encuentra aplicado.", result.Message);
        Assert.Equal("Applied", result.Status);
        clientMock.Verify(c => c.GetTodayRoutineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        clientMock.Verify(c => c.ApplyAthleteSessionAdjustmentAsync(It.IsAny<Guid>(), It.IsAny<ApplyAthleteAdjustmentRequestRemoteDto>(), It.IsAny<CancellationToken>()), Times.Never);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_ApplyAlertAdjustmentCommand_When_NoSessionToday_Then_ThrowsDomainException()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);

        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.GetByIdAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var clientMock = new Mock<ITrainingPlanServiceClient>();
        clientMock.Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TodayRoutineRemoteDto?)null);

        var uowMock = new Mock<IUnitOfWork>();

        var handler = new ApplyAlertAdjustmentCommandHandler(repoMock.Object, clientMock.Object, uowMock.Object);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new ApplyAlertAdjustmentCommand(alert.Id)));

        Assert.Contains("no tiene una sesión de entrenamiento programada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AlertStatus.Pending, alert.Status);
    }

    // -------------------------------------------------------------------------
    // 4. CONTROLADOR Y CONTRATOS REST / SEGURIDAD
    // -------------------------------------------------------------------------

    [Fact]
    public void Given_AlertsController_When_InspectingAttributes_Then_RequiresPreparadorFisicoRole()
    {
        var authAttribute = typeof(AlertsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);
        Assert.Equal("PreparadorFisico", authAttribute.Roles);

        var routeAttribute = typeof(AlertsController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttribute);
        Assert.Equal("api/v1/alerts", routeAttribute.Template);
    }

    [Fact]
    public async Task Given_AlertsController_When_ListCalled_Then_ReturnsOkWithAlerts()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.ListAsync(true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([alert]);

        var applyHandler = new ApplyAlertAdjustmentCommandHandler(
            repoMock.Object,
            new Mock<ITrainingPlanServiceClient>().Object,
            new Mock<IUnitOfWork>().Object);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.PfId).Returns(PfId);

        var controller = new AlertsController(
            new GetAlertsQueryHandler(repoMock.Object),
            new GetAlertByIdQueryHandler(repoMock.Object),
            new ReviewAlertCommandHandler(repoMock.Object, new Mock<IUnitOfWork>().Object),
            new CreatePreventiveAlertCommandHandler(repoMock.Object, structureMock.Object, currentUserMock.Object, new Mock<IUnitOfWork>().Object),
            applyHandler);

        var actionResult = await controller.List(onlyUnreviewed: true);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<PreventiveAlertResponseDto>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal(AthleteName, list[0].AthleteName);
    }

    [Fact]
    public async Task Given_AlertsController_When_CreateCalled_Then_ReturnsCreatedAtAction()
    {
        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var applyHandler = new ApplyAlertAdjustmentCommandHandler(
            repoMock.Object,
            new Mock<ITrainingPlanServiceClient>().Object,
            uowMock.Object);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.PfId).Returns(PfId);

        var controller = new AlertsController(
            new GetAlertsQueryHandler(repoMock.Object),
            new GetAlertByIdQueryHandler(repoMock.Object),
            new ReviewAlertCommandHandler(repoMock.Object, uowMock.Object),
            new CreatePreventiveAlertCommandHandler(repoMock.Object, structureMock.Object, currentUserMock.Object, uowMock.Object),
            applyHandler);

        var request = new CreatePreventiveAlertRequestDto(
            AthleteId: AthleteId,
            AthleteName: AthleteName,
            Reason: ReasonFatigue,
            Suggestion: SuggestionAdjustment,
            Severity: "High",
            Type: "AccumulatedFatigue",
            SuggestedAdjustmentType: "LoadReduction",
            SuggestedAdjustmentPercentage: 20);

        var actionResult = await controller.Create(request, CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        var dto = Assert.IsType<PreventiveAlertResponseDto>(createdResult.Value);

        Assert.Equal(AthleteName, dto.AthleteName);
        Assert.Equal("LoadReduction", dto.SuggestedAdjustmentType);
        Assert.Equal(20, dto.SuggestedAdjustmentPercentage);
    }

    [Fact]
    public async Task Given_AlertsController_When_ApplyAdjustmentCalled_Then_ReturnsOkWithResult()
    {
        var alert = PreventiveAlert.Create(AthleteId, AthleteName, ReasonFatigue, SuggestionAdjustment);
        var repoMock = new Mock<IPreventiveAlertRepository>();
        repoMock.Setup(r => r.GetByIdAsync(alert.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(alert);

        var sessionId = Guid.NewGuid();
        var clientMock = new Mock<ITrainingPlanServiceClient>();
        clientMock.Setup(c => c.GetTodayRoutineAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TodayRoutineRemoteDto(AthleteId, Domain.Common.ArgentinaTime.Today, [], sessionId));
        clientMock.Setup(c => c.ApplyAthleteSessionAdjustmentAsync(sessionId, It.IsAny<ApplyAthleteAdjustmentRequestRemoteDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var uowMock = new Mock<IUnitOfWork>();
        uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var applyHandler = new ApplyAlertAdjustmentCommandHandler(repoMock.Object, clientMock.Object, uowMock.Object);

        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.PfId).Returns(PfId);

        var controller = new AlertsController(
            new GetAlertsQueryHandler(repoMock.Object),
            new GetAlertByIdQueryHandler(repoMock.Object),
            new ReviewAlertCommandHandler(repoMock.Object, uowMock.Object),
            new CreatePreventiveAlertCommandHandler(repoMock.Object, structureMock.Object, currentUserMock.Object, uowMock.Object),
            applyHandler);

        var actionResult = await controller.Apply(alert.Id, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dto = Assert.IsType<ApplyAlertAdjustmentResponseDto>(okResult.Value);

        Assert.Equal(alert.Id, dto.AlertId);
        Assert.Equal("Applied", dto.Status);
    }

    [Fact]
    public async Task Given_AthleteBelongsToAnotherPf_When_CreateAlertCommand_Then_ThrowsNotFoundAndDoesNotPersist()
    {
        var repoMock = new Mock<IPreventiveAlertRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var structureMock = new Mock<IStructureAthleteClient>();
        structureMock.Setup(s => s.IsOwnAthleteAsync(AthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.PfId).Returns(PfId);

        var handler = new CreatePreventiveAlertCommandHandler(
            repoMock.Object, structureMock.Object, currentUserMock.Object, uowMock.Object);

        var request = new CreatePreventiveAlertRequestDto(
            AthleteId: AthleteId,
            AthleteName: AthleteName,
            Reason: ReasonFatigue,
            Suggestion: SuggestionAdjustment);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new CreatePreventiveAlertCommand(request)));

        repoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Critical", null, null)]
    [InlineData(null, "UnknownType", null)]
    [InlineData(null, null, "MagicFix")]
    public async Task Given_InvalidEnumValue_When_CreateAlertCommand_Then_ThrowsDomainExceptionWithoutCallingStructure(
        string? severity,
        string? type,
        string? adjustmentType)
    {
        var repoMock = new Mock<IPreventiveAlertRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var structureMock = new Mock<IStructureAthleteClient>();
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.PfId).Returns(PfId);

        var handler = new CreatePreventiveAlertCommandHandler(
            repoMock.Object, structureMock.Object, currentUserMock.Object, uowMock.Object);

        var request = new CreatePreventiveAlertRequestDto(
            AthleteId: AthleteId,
            AthleteName: AthleteName,
            Reason: ReasonFatigue,
            Suggestion: SuggestionAdjustment,
            Severity: severity,
            Type: type,
            SuggestedAdjustmentType: adjustmentType);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.HandleAsync(new CreatePreventiveAlertCommand(request)));

        structureMock.Verify(s => s.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_MissingPfId_When_CreateAlertCommand_Then_ThrowsUnauthorizedWithoutCallingStructure()
    {
        var repoMock = new Mock<IPreventiveAlertRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        var structureMock = new Mock<IStructureAthleteClient>();
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.PfId).Returns((Guid?)null);

        var handler = new CreatePreventiveAlertCommandHandler(
            repoMock.Object, structureMock.Object, currentUserMock.Object, uowMock.Object);

        var request = new CreatePreventiveAlertRequestDto(
            AthleteId: AthleteId,
            AthleteName: AthleteName,
            Reason: ReasonFatigue,
            Suggestion: SuggestionAdjustment);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.HandleAsync(new CreatePreventiveAlertCommand(request)));

        structureMock.Verify(s => s.IsOwnAthleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        repoMock.Verify(r => r.AddAsync(It.IsAny<PreventiveAlert>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

