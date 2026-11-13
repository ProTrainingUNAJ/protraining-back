using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using API.Controllers;
using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Tests;

public class InternalCheckInsControllerTests
{
    private readonly Mock<ICheckInRepository> _checkInRepositoryMock = new();
    private readonly InternalCheckInsController _controller;

    public InternalCheckInsControllerTests()
    {
        _controller = new InternalCheckInsController(_checkInRepositoryMock.Object);
    }

    [Fact]
    public void Controller_RequiereRolServicio_RechazaOtrosRolesCon403()
    {
        // Verifica que el endpoint interno este protegido estrictamente para el rol Servicio,
        // garantizando que cualquier usuario comun (PF o Atleta) reciba 403 Forbidden.
        var authorizeAttribute = typeof(InternalCheckInsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(authorizeAttribute);
        Assert.Equal("Servicio", authorizeAttribute.Roles);
    }

    [Fact]
    public async Task GetCompletedCheckIns_PfIdVacio_Devuelve400()
    {
        var request = new CheckInsDoneRequest(new DateOnly(2026, 10, 28), Guid.Empty, [Guid.NewGuid()]);
        var result = await _controller.GetCompletedCheckIns(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task GetCompletedCheckIns_Aislamiento_ConPfIdDeUnPfNoDevuelveCheckInsDeOtro()
    {
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 28);
        var athleteOfPfA = Guid.NewGuid();

        _checkInRepositoryMock
            .Setup(r => r.GetCompletedCheckInAthleteIdsAsync(date, pfA, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([athleteOfPfA]);

        _checkInRepositoryMock
            .Setup(r => r.GetCompletedCheckInAthleteIdsAsync(date, pfB, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Consultamos con PF B
        var requestPfB = new CheckInsDoneRequest(date, pfB, [athleteOfPfA]);
        var resultPfB = await _controller.GetCompletedCheckIns(requestPfB, CancellationToken.None);
        var okPfB = Assert.IsType<OkObjectResult>(resultPfB.Result);
        var responsePfB = Assert.IsType<CheckInsDoneResponse>(okPfB.Value);
        Assert.Empty(responsePfB.AthleteIds);

        // Consultamos con PF A
        var requestPfA = new CheckInsDoneRequest(date, pfA, [athleteOfPfA]);
        var resultPfA = await _controller.GetCompletedCheckIns(requestPfA, CancellationToken.None);
        var okPfA = Assert.IsType<OkObjectResult>(resultPfA.Result);
        var responsePfA = Assert.IsType<CheckInsDoneResponse>(okPfA.Value);
        Assert.Contains(athleteOfPfA, responsePfA.AthleteIds);
    }

    [Fact]
    public async Task GetCheckInsHistory_ListaAtletasVacia_DevuelveOkVacioSinConsultarDb()
    {
        var request = new CheckInsHistoryRequest([], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7));
        var result = await _controller.GetCheckInsHistory(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<InternalCheckInHistoryDto>>(okResult.Value);
        Assert.Empty(items);

        _checkInRepositoryMock.Verify(
            r => r.GetHistoryByAthletesAndDateRangeAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCheckInsHistory_AtletasValidos_MapeaCamposCorrectamente()
    {
        var athleteId = Guid.NewGuid();
        var date = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        var checkIn = new CheckIn
        {
            AthleteId = athleteId,
            Date = date,
            SleepHours = 5.5,
            SleepQuality = 2,
            MuscleSoreness = 4,
            FatigueLevel = 4,
            MoodRate = 3,
            StressLevel = 4,
            EffortRate = 9,
            IsSick = false,
            PainZones = "Rodilla derecha",
            HasActiveInjury = true
        };

        _checkInRepositoryMock
            .Setup(r => r.GetHistoryByAthletesAndDateRangeAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(athleteId)),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([checkIn]);

        var request = new CheckInsHistoryRequest([athleteId], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7));
        var result = await _controller.GetCheckInsHistory(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<InternalCheckInHistoryDto>>(okResult.Value);
        Assert.Single(items);

        var first = items[0];
        Assert.Equal(athleteId, first.AthleteId);
        Assert.Equal(new DateOnly(2026, 11, 4), first.Date);
        Assert.Equal(5.5, first.SleepHours);
        Assert.Equal(9, first.EffortRate);
        Assert.Equal(4, first.FatigueLevel);
        Assert.True(first.HasActiveInjury);
        Assert.Equal("Rodilla derecha", first.PainZones);
    }
}
