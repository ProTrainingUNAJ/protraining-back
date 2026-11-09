using System.Security.Claims;
using API.Controllers.DailyLoads;
using Application.DTOs.DailyLoad;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.UseCases.DailyLoads.GetAthleteWeek;
using Application.UseCases.DailyLoads.RegisterDailyLoad;
using Domain.Common;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Seguimiento.Tests;

public class DailyLoadsControllerTests
{
    private readonly Mock<IDailyLoadRepository> _loads = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStructureClient> _estructura = new();
    private readonly GetAthleteWeekQueryHandler _weekHandler;

    public DailyLoadsControllerTests()
    {
        _weekHandler = new GetAthleteWeekQueryHandler(_loads.Object);
    }

    private DailyLoadsController CreateController()
    {
        var handler = new RegisterDailyLoadCommandHandler(_loads.Object, _unitOfWork.Object);
        var controller = new DailyLoadsController(handler, _weekHandler, _estructura.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private void ResponderMe(Guid athleteId) =>
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = athleteId, FullName = "Ana Pérez" });

    private void ConfigurarNuevo()
    {
        _loads
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyLoad?)null);
        _loads
            .Setup(r => r.AddAsync(It.IsAny<DailyLoad>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private static RegisterDailyLoadRequest RequestValido() => new()
    {
        Gym = new ActivityLoadDto { Minutes = 60, Rpe = 7 },
    };

    [Fact]
    public async Task Register_WithTokenOfSameAthlete_Returns_201()
    {
        var athleteId = Guid.NewGuid();
        ResponderMe(athleteId);
        ConfigurarNuevo();

        var result = await CreateController().Register(athleteId, RequestValido(), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    [Trait("Category", "Isolation")]
    public async Task Register_WithTokenOfAnotherAthlete_Throws_Forbidden_And_Does_Not_Save()
    {
        ResponderMe(Guid.NewGuid());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateController().Register(Guid.NewGuid(), RequestValido(), CancellationToken.None));

        _loads.Verify(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _loads.Verify(r => r.AddAsync(It.IsAny<DailyLoad>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_WithoutLoadToday_Throws_DailyLoadNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        ResponderMe(athleteId);
        _loads
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyLoad?)null);

        await Assert.ThrowsAsync<DailyLoadNotFoundException>(() =>
            CreateController().Get(athleteId, CancellationToken.None));
    }
}