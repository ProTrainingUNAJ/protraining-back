using Application.DTOs.DailyLoad;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.UseCases.DailyLoads.RegisterDailyLoad;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Seguimiento.Tests;

public class RegisterDailyLoadCommandHandlerTests
{
    private readonly Mock<IDailyLoadRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private RegisterDailyLoadCommandHandler CreateHandler() =>
        new(_repo.Object, _unitOfWork.Object);

    private static RegisterDailyLoadRequest RequestValido() => new()
    {
        Gym = new ActivityLoadDto { Minutes = 60, Rpe = 7 },
    };

    private void ConfigurarNuevo()
    {
        _repo
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyLoad?)null);
        _repo
            .Setup(r => r.AddAsync(It.IsAny<DailyLoad>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    [Fact]
    public async Task HandleAsync_FirstLoad_ReturnsCreatedTrue()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarNuevo();

        var result = await CreateHandler().HandleAsync(
            new RegisterDailyLoadCommand(athleteId, RequestValido()), CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(420, result.Response.TotalLoad);
    }

    [Fact]
    public async Task HandleAsync_SecondLoadSameDay_UpdatesAndReturnsCreatedFalse()
    {
        var athleteId = Guid.NewGuid();
        var existing = new DailyLoad { AthleteId = athleteId, Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue) };
        _repo
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateHandler().HandleAsync(
            new RegisterDailyLoadCommand(athleteId, RequestValido()), CancellationToken.None);

        Assert.False(result.Created);
        _repo.Verify(r => r.AddAsync(It.IsAny<DailyLoad>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(60, existing.GymMinutes);
        Assert.Equal(7, existing.GymRpe);
    }

    [Fact]
    public async Task HandleAsync_UsesArgentinaDate()
    {
        var athleteId = Guid.NewGuid();
        DateTime? receivedDate = null;
        _repo
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, CancellationToken>((_, date, _) => receivedDate = date)
            .ReturnsAsync((DailyLoad?)null);
        _repo
            .Setup(r => r.AddAsync(It.IsAny<DailyLoad>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await CreateHandler().HandleAsync(
            new RegisterDailyLoadCommand(athleteId, RequestValido()), CancellationToken.None);

        Assert.NotNull(receivedDate);
        Assert.Equal(ArgentinaTime.Today, DateOnly.FromDateTime(receivedDate!.Value));
    }

    [Fact]
    public async Task HandleAsync_MultipleActivities_TotalLoadSum()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarNuevo();
        var request = new RegisterDailyLoadRequest
        {
            Gym = new ActivityLoadDto { Minutes = 60, Rpe = 7 },
            Match1 = new ActivityLoadDto { Minutes = 90, Rpe = 8 },
        };

        var result = await CreateHandler().HandleAsync(
            new RegisterDailyLoadCommand(athleteId, request), CancellationToken.None);

        Assert.Equal(1140, result.Response.TotalLoad);
    }

    [Fact]
    public async Task HandleAsync_NegativeMinutes_ThrowsDomainException()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarNuevo();
        var request = new RegisterDailyLoadRequest
        {
            Gym = new ActivityLoadDto { Minutes = -1, Rpe = 7 },
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler().HandleAsync(new RegisterDailyLoadCommand(athleteId, request), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_RpeOutOfRange_ThrowsDomainException()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarNuevo();
        var request = new RegisterDailyLoadRequest
        {
            Gym = new ActivityLoadDto { Minutes = 60, Rpe = 15 },
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler().HandleAsync(new RegisterDailyLoadCommand(athleteId, request), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MinutesWithoutRpe_ThrowsDomainException()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarNuevo();
        var request = new RegisterDailyLoadRequest
        {
            Gym = new ActivityLoadDto { Minutes = 60, Rpe = null },
        };

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler().HandleAsync(new RegisterDailyLoadCommand(athleteId, request), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_AllZero_ThrowsDomainException()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarNuevo();

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateHandler().HandleAsync(new RegisterDailyLoadCommand(athleteId, new RegisterDailyLoadRequest()), CancellationToken.None));
    }

    [Fact]
    public async Task GetTodayAsync_WhenNoLoad_ThrowsDailyLoadNotFoundException()
    {
        var athleteId = Guid.NewGuid();
        _repo
            .Setup(r => r.GetByAthleteAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyLoad?)null);

        await Assert.ThrowsAsync<DailyLoadNotFoundException>(() =>
            CreateHandler().GetTodayAsync(athleteId, CancellationToken.None));
    }
}