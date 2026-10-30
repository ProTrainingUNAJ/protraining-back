using System.Linq;
using System.Security.Claims;
using API.Controllers;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.TrainingSessions.GetAthletesWithScheduledSession;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Tests;

public class InternalTrainingSessionsControllerTests
{
    private readonly Mock<ITrainingSessionRepository> _sessionRepositoryMock = new();
    private readonly Mock<IStructureClient> _structureClientMock = new();
    private readonly GetAthletesWithScheduledSessionQueryHandler _handler;
    private readonly InternalTrainingSessionsController _controller;

    public InternalTrainingSessionsControllerTests()
    {
        _handler = new GetAthletesWithScheduledSessionQueryHandler(
            _sessionRepositoryMock.Object,
            _structureClientMock.Object);

        _controller = new InternalTrainingSessionsController(_handler);
    }

    [Fact]
    public void Controller_RequiereRolServicio_RechazaOtrosRolesCon403()
    {
        // Verifica que el endpoint interno este protegido estrictamente para el rol Servicio,
        // garantizando que cualquier usuario comun (PF o Atleta) reciba 403 Forbidden.
        var authorizeAttribute = typeof(InternalTrainingSessionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal("Servicio", authorizeAttribute.Roles);
    }

    [Fact]
    public async Task GetAthletesWithScheduledSession_ParametrosFaltantes_Devuelve400()
    {
        var result = await _controller.GetAthletesWithScheduledSession(null, null, CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task GetAthletesWithScheduledSession_Aislamiento_ConPfIdDeUnPfNoDevuelveDatosDeOtro()
    {
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 28);
        var athleteOfPfA = Guid.NewGuid();

        // Plan de PF A con sesion hoy asignado a su atleta
        var plan = TrainingPlan.Create("Plan PF A", null);
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Fuerza", 1, date.DayOfWeek);
        week.AddSession(session);
        plan.AddWeek(week);

        var assignmentPfA = PlanAssignment.Create(plan.Id, date, athleteId: athleteOfPfA);
        typeof(PlanAssignment).GetProperty("Plan")!.SetValue(assignmentPfA, plan);

        // Simulamos que el repositorio solo devuelve asignaciones del PF consultado
        _sessionRepositoryMock
            .Setup(r => r.ListAssignmentsWithPlansByPfIdAsync(pfA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanAssignment> { assignmentPfA });

        _sessionRepositoryMock
            .Setup(r => r.ListAssignmentsWithPlansByPfIdAsync(pfB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanAssignment>());

        // Consultamos con PF B
        var resultPfB = await _controller.GetAthletesWithScheduledSession(date, pfB, CancellationToken.None);
        var okPfB = Assert.IsType<OkObjectResult>(resultPfB.Result);
        var athletesPfB = Assert.IsAssignableFrom<IEnumerable<Guid>>(okPfB.Value);
        Assert.Empty(athletesPfB);

        // Consultamos con PF A
        var resultPfA = await _controller.GetAthletesWithScheduledSession(date, pfA, CancellationToken.None);
        var okPfA = Assert.IsType<OkObjectResult>(resultPfA.Result);
        var athletesPfA = Assert.IsAssignableFrom<IEnumerable<Guid>>(okPfA.Value);
        Assert.Contains(athleteOfPfA, athletesPfA);
    }
}