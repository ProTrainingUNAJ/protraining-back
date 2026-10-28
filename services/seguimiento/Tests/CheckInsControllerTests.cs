using API.Controllers.CheckIns;
using Application.DTOs.CheckIns;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Domain.Entities;
using Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Seguimiento.Tests;

// Seguridad del endpoint (KAN-329): el atleta del token manda sobre el athleteId de la URL.
public class CheckInsControllerTests
{
    private readonly Mock<ICheckInRepository> _checkIns = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStructureClient> _estructura = new();
    private readonly TrafficLightEngine _engine = new();

    private CheckInsController CreateController() =>
        new(new SubmitCheckInCommandHandler(_checkIns.Object, _unitOfWork.Object, _engine), _estructura.Object);

    private static SubmitCheckInRequest RequestValida() => new()
    {
        SleepHours = 8,
        SleepQuality = 4,
        MuscleSoreness = 1,
        FatigueLevel = 2,
        MoodRate = 4,
        StressLevel = 2,
        EffortRate = 4
    };

    private void ResponderMe(Guid athleteId) =>
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto { AthleteId = athleteId, FullName = "Ana Pérez" });

    private void ConfigurarCheckInNuevo()
    {
        _checkIns
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckIn?)null);
        _checkIns
            .Setup(r => r.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task Submit_WithTokenOfTheSameAthlete_Returns_201()
    {
        var athleteId = Guid.NewGuid();
        ResponderMe(athleteId);
        ConfigurarCheckInNuevo();

        var result = await CreateController().Submit(athleteId, RequestValida(), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    // KAN-431: el id de otro atleta responde 404 (no 403), para no revelar si existe.
    public async Task Submit_WithTokenOfAnotherAthlete_Throws_AthleteNotFound_And_Does_Not_Save()
    {
        ResponderMe(Guid.NewGuid());

        await Assert.ThrowsAsync<AthleteNotFoundException>(() =>
            CreateController().Submit(Guid.NewGuid(), RequestValida(), CancellationToken.None));

        _checkIns.Verify(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _checkIns.Verify(r => r.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_WithoutLinkedAthlete_Throws_NoAthleteLinked()
    {
        _estructura
            .Setup(s => s.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteMeDto?)null);

        await Assert.ThrowsAsync<NoAthleteLinkedException>(() =>
            CreateController().Submit(Guid.NewGuid(), RequestValida(), CancellationToken.None));

        _checkIns.Verify(r => r.AddAsync(It.IsAny<CheckIn>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Submit_WhenTodaysCheckInExists_Returns_200()
    {
        var athleteId = Guid.NewGuid();
        ResponderMe(athleteId);
        _checkIns
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckIn { AthleteId = athleteId, Date = DateTime.Today });
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateController().Submit(athleteId, RequestValida(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, ok.StatusCode);
    }
}