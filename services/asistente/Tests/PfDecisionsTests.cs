using System.Net;
using System.Reflection;
using API.Controllers;
using Application.Common.PlanDrafts;
using Application.DTOs.PfDecisions;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Ai.SuggestRoutine;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
using Application.UseCases.PfDecisions.RecordDayAdjustmentDecision;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Asistente.Tests;

/// <summary>
/// Pruebas automatizadas para la memoria de decisiones del PF (KAN-423 · Épica E37).
/// Cubre los 3 criterios de aceptación de Jira, reglas de dominio, ordenamiento,
/// filtro por zona, tope de 50 decisiones y seguridad multi-tenant.
/// </summary>
public sealed class PfDecisionsTests
{
    private static readonly Guid MyAthleteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherAthleteId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid DeadliftId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GluteBridgeId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid SquatId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid AdjustmentId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    // =========================================================================
    // 1. CRITERIO DE ACEPTACIÓN 1 (Jira KAN-423)
    // DADO un PF que aprueba un ajuste cambiando el sustituto sugerido
    // CUANDO se guarda
    // ENTONCES queda una decisión Modified con la zona, el ejercicio sugerido y el elegido.
    // =========================================================================
    [Fact]
    public async Task Given_PfApprovesDayAdjustmentWithSubstitute_When_Recorded_Then_CreatesModifiedDecisionWithZoneSuggestedAndFinal()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        PfDecision? savedDecision = null;

        decisionRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PfDecision>(), It.IsAny<CancellationToken>()))
            .Callback<PfDecision, CancellationToken>((d, _) => savedDecision = d)
            .Returns(Task.CompletedTask);

        var handler = new RecordDayAdjustmentDecisionCommandHandler(decisionRepoMock.Object, uowMock.Object);

        var command = new RecordDayAdjustmentDecisionCommand(
            AthleteId: MyAthleteId,
            SourceId: AdjustmentId,
            SuggestedExerciseId: DeadliftId,
            Decision: PfDecisionType.Modified,
            FinalExerciseId: GluteBridgeId,
            FinalPercentage: 70m,
            Zone: "espalda_baja",
            Reason: "Molestia lumbar moderada");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(savedDecision);
        Assert.Equal(PfDecisionType.Modified, savedDecision.Decision);
        Assert.Equal(PfDecisionSource.DayAdjustment, savedDecision.Source);
        Assert.Equal(MyAthleteId, savedDecision.AthleteId);
        Assert.Equal(AdjustmentId, savedDecision.SourceId);
        Assert.Equal("espalda_baja", savedDecision.Zone);
        Assert.Equal(DeadliftId, savedDecision.SuggestedExerciseId);
        Assert.Equal(GluteBridgeId, savedDecision.FinalExerciseId);
        Assert.Equal(70m, savedDecision.FinalPercentage);
        Assert.Equal("Molestia lumbar moderada", savedDecision.Reason);

        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("Modified", result.Decision);
        Assert.Equal(GluteBridgeId, result.FinalExerciseId);
        Assert.Equal("espalda_baja", result.Zone);
    }

    // =========================================================================
    // 2. CRITERIO DE ACEPTACIÓN 2 (Jira KAN-423)
    // DADO un PF que rechaza un ajuste con el motivo "Prefiero que descanse"
    // CUANDO se guarda
    // ENTONCES queda Rejected con ese motivo.
    // =========================================================================
    [Fact]
    public async Task Given_PfRejectsDayAdjustmentWithReason_When_Recorded_Then_CreatesRejectedDecisionWithReason()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var uowMock = new Mock<IUnitOfWork>();
        PfDecision? savedDecision = null;

        decisionRepoMock
            .Setup(r => r.AddAsync(It.IsAny<PfDecision>(), It.IsAny<CancellationToken>()))
            .Callback<PfDecision, CancellationToken>((d, _) => savedDecision = d)
            .Returns(Task.CompletedTask);

        var handler = new RecordDayAdjustmentDecisionCommandHandler(decisionRepoMock.Object, uowMock.Object);

        var command = new RecordDayAdjustmentDecisionCommand(
            AthleteId: MyAthleteId,
            SourceId: AdjustmentId,
            SuggestedExerciseId: SquatId,
            Decision: PfDecisionType.Rejected,
            Reason: "Prefiero que descanse",
            Zone: "rodilla");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(savedDecision);
        Assert.Equal(PfDecisionType.Rejected, savedDecision.Decision);
        Assert.Equal("Prefiero que descanse", savedDecision.Reason);
        Assert.Null(savedDecision.FinalExerciseId);
        Assert.Equal("rodilla", savedDecision.Zone);

        uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal("Rejected", result.Decision);
        Assert.Equal("Prefiero que descanse", result.Reason);
    }

    // =========================================================================
    // 3. CRITERIO DE ACEPTACIÓN 3 (Jira KAN-423)
    // DADO otro PF CUANDO pide las decisiones de ese atleta ENTONCES recibe 404.
    // =========================================================================
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_PfAsksForDecisionsOfAnotherPfAthlete_When_Querying_Then_ThrowsNotFoundException404()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();

        // Estructura responde que el atleta NO pertenece a este PF (o no existe)
        structureClientMock
            .Setup(c => c.IsOwnAthleteAsync(OtherAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);

        // Act & Assert: Debe arrojar NotFoundException (HTTP 404), nunca 403 ni 200 con lista vacía
        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetAthleteDecisionsQuery(OtherAthleteId)));

        Assert.Contains("no pertenece", exception.Message);
        decisionRepoMock.Verify(r => r.GetRecentByAthleteAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Given_EmptyAthleteId_When_Querying_Then_ThrowsNotFoundException404()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();
        var handler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.HandleAsync(new GetAthleteDecisionsQuery(Guid.Empty)));
    }

    // =========================================================================
    // 4. FILTRO POR ZONA (?zone=espalda_baja)
    // =========================================================================
    [Fact]
    public async Task Given_AthleteWithDecisionsInDifferentZones_When_FilteringByZone_Then_ReturnsOnlyDecisionsForThatZone()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();

        structureClientMock
            .Setup(c => c.IsOwnAthleteAsync(MyAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var decisionLumbar = PfDecision.Create(
            athleteId: MyAthleteId,
            source: PfDecisionSource.DayAdjustment,
            sourceId: AdjustmentId,
            suggestedExerciseId: DeadliftId,
            decision: PfDecisionType.Modified,
            finalExerciseId: GluteBridgeId,
            zone: "espalda_baja");

        decisionRepoMock
            .Setup(r => r.GetRecentByAthleteAsync(MyAthleteId, "espalda_baja", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([decisionLumbar]);

        var handler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);

        // Act
        var result = await handler.HandleAsync(new GetAthleteDecisionsQuery(MyAthleteId, "espalda_baja"));

        // Assert
        Assert.Single(result);
        Assert.Equal("espalda_baja", result[0].Zone);
        Assert.Equal("Modified", result[0].Decision);
    }

    // =========================================================================
    // 5. TOPE DE ÚLTIMAS 50 DECISIONES
    // =========================================================================
    [Fact]
    public async Task Given_AthleteWithMoreThan50Decisions_When_Querying_Then_LimitsToLast50()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();

        structureClientMock
            .Setup(c => c.IsOwnAthleteAsync(MyAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var fiftyDecisions = Enumerable.Range(1, 50)
            .Select(i => PfDecision.Create(
                athleteId: MyAthleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: Guid.NewGuid(),
                suggestedExerciseId: DeadliftId,
                decision: PfDecisionType.Approved,
                date: new DateOnly(2026, 10, 1).AddDays(i)))
            .ToList();

        decisionRepoMock
            .Setup(r => r.GetRecentByAthleteAsync(MyAthleteId, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fiftyDecisions);

        var handler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);

        // Act
        var result = await handler.HandleAsync(new GetAthleteDecisionsQuery(MyAthleteId));

        // Assert
        Assert.Equal(50, result.Count);
        decisionRepoMock.Verify(r => r.GetRecentByAthleteAsync(MyAthleteId, null, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // 6. ATLETA PROPIO SIN DECISIONES PREVIAS
    // =========================================================================
    [Fact]
    public async Task Given_OwnAthleteWithoutDecisions_When_Querying_Then_ReturnsEmptyListWithOk200()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();

        structureClientMock
            .Setup(c => c.IsOwnAthleteAsync(MyAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        decisionRepoMock
            .Setup(r => r.GetRecentByAthleteAsync(MyAthleteId, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var handler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);

        // Act
        var result = await handler.HandleAsync(new GetAthleteDecisionsQuery(MyAthleteId));

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // =========================================================================
    // 7. REGLAS DE DOMINIO EN PFDECISION
    // =========================================================================
    [Fact]
    public void Given_ModifiedDecisionWithoutFinalExercise_When_Creating_Then_ThrowsDomainException()
    {
        // Act & Assert
        var ex = Assert.Throws<DomainException>(() =>
            PfDecision.Create(
                athleteId: MyAthleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: AdjustmentId,
                suggestedExerciseId: DeadliftId,
                decision: PfDecisionType.Modified,
                finalExerciseId: null));

        Assert.Contains("requiere indicar el ejercicio final", ex.Message);
    }

    [Fact]
    public void Given_DecisionWithEmptySourceOrExerciseId_When_Creating_Then_ThrowsDomainException()
    {
        // Empty SourceId
        Assert.Throws<DomainException>(() =>
            PfDecision.Create(
                athleteId: MyAthleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: Guid.Empty,
                suggestedExerciseId: DeadliftId,
                decision: PfDecisionType.Approved));

        // Empty SuggestedExerciseId
        Assert.Throws<DomainException>(() =>
            PfDecision.Create(
                athleteId: MyAthleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: AdjustmentId,
                suggestedExerciseId: Guid.Empty,
                decision: PfDecisionType.Approved));
    }

    // =========================================================================
    // 8. DECISIONES PROVENIENTES DE PLANDRAFT (PfDecisionDiffExtractor)
    // =========================================================================
    [Fact]
    public void Given_PlanDraft_When_PfReplacesAndRemovesExercises_Then_ExtractsModifiedAndRejectedDecisions()
    {
        // Arrange
        var draftId = Guid.NewGuid();
        var prevContent = new PlanDraftContentDto(
            Name: "Plan Previo",
            Objective: "Fuerza",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión 1",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(DeadliftId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 80m),
                                new PlanDraftExerciseDto(SquatId, Order: 2, Sets: 3, Reps: 8, TargetPercentage: 70m)
                            ])
                    ])
            ]);

        // Ejercicio 1 sustituido por GluteBridge, Ejercicio 2 eliminado
        var updatedContent = new PlanDraftContentDto(
            Name: "Plan Actualizado",
            Objective: "Fuerza adaptada",
            JustificationSummary: "Resumen",
            Weeks:
            [
                new PlanDraftWeekDto(
                    WeekNumber: 1,
                    Sessions:
                    [
                        new PlanDraftSessionDto(
                            Name: "Sesión 1",
                            DayOfWeek: DayOfWeek.Monday,
                            Order: 1,
                            CoachNotes: null,
                            Exercises:
                            [
                                new PlanDraftExerciseDto(GluteBridgeId, Order: 1, Sets: 4, Reps: 6, TargetPercentage: 75m)
                            ])
                    ])
            ]);

        // Act
        var decisions = PfDecisionDiffExtractor.ExtractDecisions(draftId, MyAthleteId, prevContent, updatedContent);

        // Assert
        Assert.Equal(2, decisions.Count);

        var modified = Assert.Single(decisions, d => d.Decision == PfDecisionType.Modified);
        Assert.Equal(DeadliftId, modified.SuggestedExerciseId);
        Assert.Equal(GluteBridgeId, modified.FinalExerciseId);
        Assert.Equal(75m, modified.FinalPercentage);
        Assert.Equal(PfDecisionSource.PlanDraft, modified.Source);
        Assert.Equal(MyAthleteId, modified.AthleteId);

        var rejected = Assert.Single(decisions, d => d.Decision == PfDecisionType.Rejected);
        Assert.Equal(SquatId, rejected.SuggestedExerciseId);
        Assert.Null(rejected.FinalExerciseId);
        Assert.Equal(PfDecisionSource.PlanDraft, rejected.Source);
    }

    // =========================================================================
    // 9. ENDPOINT EN ASSISTANTCONTROLLER
    // =========================================================================
    [Fact]
    public async Task Given_AssistantController_When_GettingAthleteDecisionsOfOwnAthlete_Then_ReturnsOkWithDecisionsList()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();

        structureClientMock
            .Setup(c => c.IsOwnAthleteAsync(MyAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var decision = PfDecision.Create(
            athleteId: MyAthleteId,
            source: PfDecisionSource.DayAdjustment,
            sourceId: AdjustmentId,
            suggestedExerciseId: DeadliftId,
            decision: PfDecisionType.Modified,
            finalExerciseId: GluteBridgeId,
            zone: "espalda_baja");

        decisionRepoMock
            .Setup(r => r.GetRecentByAthleteAsync(MyAthleteId, "espalda_baja", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([decision]);

        var queryHandler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);
        var suggestUseCase = new SuggestRoutineUseCase(Mock.Of<IAiService>(), Mock.Of<IRoutineDraftRepository>(), Mock.Of<IUnitOfWork>());

        var controller = new AssistantController(suggestUseCase, queryHandler);

        // Act
        var actionResult = await controller.GetAthleteDecisions(MyAthleteId, "espalda_baja", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dtos = Assert.IsAssignableFrom<IReadOnlyList<PfDecisionResponseDto>>(okResult.Value);
        Assert.Single(dtos);
        Assert.Equal("espalda_baja", dtos[0].Zone);
        Assert.Equal("Modified", dtos[0].Decision);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Given_AssistantController_When_GettingAthleteDecisionsOfAnotherPf_Then_ThrowsNotFoundException()
    {
        // Arrange
        var decisionRepoMock = new Mock<IPfDecisionRepository>();
        var structureClientMock = new Mock<IStructureAthleteClient>();

        structureClientMock
            .Setup(c => c.IsOwnAthleteAsync(OtherAthleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var queryHandler = new GetAthleteDecisionsQueryHandler(decisionRepoMock.Object, structureClientMock.Object);
        var suggestUseCase = new SuggestRoutineUseCase(Mock.Of<IAiService>(), Mock.Of<IRoutineDraftRepository>(), Mock.Of<IUnitOfWork>());

        var controller = new AssistantController(suggestUseCase, queryHandler);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            controller.GetAthleteDecisions(OtherAthleteId, null, CancellationToken.None));
    }

    [Fact]
    public void Given_AssistantController_When_InspectingSecurityAndAttributes_Then_Requires_PreparadorFisico_Role()
    {
        var authAttribute = typeof(AssistantController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authAttribute);
        Assert.Equal("PreparadorFisico", authAttribute.Roles);

        var routeAttribute = typeof(AssistantController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(routeAttribute);
        Assert.Equal("api/v1/assistant", routeAttribute.Template);

        var getDecisionsMethod = typeof(AssistantController).GetMethod(nameof(AssistantController.GetAthleteDecisions));
        Assert.NotNull(getDecisionsMethod);

        var httpGet = getDecisionsMethod.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("athletes/{athleteId:guid}/decisions", httpGet.Template);
    }

    // =========================================================================
    // 10. CLIENTE HTTP DE ESTRUCTURA (StructureAthleteClient)
    // =========================================================================
    [Fact]
    public async Task Given_StructureAthleteClient_When_EstructuraReturns200_Then_ReturnsTrue()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains($"api/v1/athletes/{MyAthleteId}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8082/")
        };

        var client = new StructureAthleteClient(httpClient, Mock.Of<ILogger<StructureAthleteClient>>());

        // Act
        var result = await client.IsOwnAthleteAsync(MyAthleteId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task Given_StructureAthleteClient_When_EstructuraReturns404_Then_ReturnsFalse()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains($"api/v1/athletes/{OtherAthleteId}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8082/")
        };

        var client = new StructureAthleteClient(httpClient, Mock.Of<ILogger<StructureAthleteClient>>());

        // Act
        var result = await client.IsOwnAthleteAsync(OtherAthleteId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task Given_StructureAthleteClient_When_EstructuraReturns500_Then_ThrowsStructureServiceUnavailableException()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains($"api/v1/athletes/{MyAthleteId}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8082/")
        };

        var client = new StructureAthleteClient(httpClient, Mock.Of<ILogger<StructureAthleteClient>>());

        // Act & Assert: Debe arrojar StructureServiceUnavailableException (mapeada a HTTP 503)
        var ex = await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.IsOwnAthleteAsync(MyAthleteId));

        Assert.Contains("no se encuentra disponible", ex.Message);
    }

    [Fact]
    public async Task Given_StructureAthleteClient_When_ConnectionFails_Then_ThrowsStructureServiceUnavailableException()
    {
        // Arrange: Simular falla de conexión o caída completa de red hacia Estructura
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8082/")
        };

        var client = new StructureAthleteClient(httpClient, Mock.Of<ILogger<StructureAthleteClient>>());

        // Act & Assert
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            client.IsOwnAthleteAsync(MyAthleteId));
    }

    [Fact]
    public async Task Given_StructureAthleteClient_When_EmptyAthleteIdOrNullBaseAddress_Then_ReturnsFalseOrThrows()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8082/") };
        var client = new StructureAthleteClient(httpClient, Mock.Of<ILogger<StructureAthleteClient>>());

        Assert.False(await client.IsOwnAthleteAsync(Guid.Empty));

        var clientNoBaseAddress = new StructureAthleteClient(new HttpClient(), Mock.Of<ILogger<StructureAthleteClient>>());
        await Assert.ThrowsAsync<StructureServiceUnavailableException>(() =>
            clientNoBaseAddress.IsOwnAthleteAsync(MyAthleteId));
    }
}
