using Application.DTOs.Attendance;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Attendance.GetCategoryAttendanceToday;
using Domain.Common;
using Domain.Entities;
using Moq;

namespace Seguimiento.Tests;

public class GetCategoryAttendanceTodayQueryHandlerTests
{
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly Mock<IStructureClient> _structure = new();
    private readonly Mock<IDailyLoadRepository> _loads = new();

    private GetCategoryAttendanceTodayQueryHandler CreateHandler() =>
        new(_structure.Object, _loads.Object);

    private static CategoryAthleteDto CreateAthleteDto(Guid id, string fullName) =>
        new() { Id = id, FullName = fullName };

    private static DailyLoad CreateLoad(Guid athleteId) =>
        new() { AthleteId = athleteId, Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue) };

    private void SetupAthletes(params CategoryAthleteDto[] athletes) =>
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes.ToList());

    private void SetupLoads(params DailyLoad[] loads) =>
        _loads
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loads.ToList());

    [Fact]
    public async Task HandleAsync_MixesPresentAndMissing()
    {
        var presentId = Guid.NewGuid();
        var missingId = Guid.NewGuid();

        SetupAthletes(CreateAthleteDto(presentId, "Ana Rojas"), CreateAthleteDto(missingId, "Bruno Díaz"));
        SetupLoads(CreateLoad(presentId));

        var result = await CreateHandler().HandleAsync(new GetCategoryAttendanceTodayQuery(_categoryId));

        Assert.Equal(_categoryId, result.CategoryId);
        Assert.Equal(ArgentinaTime.Today, result.Date);
        Assert.Equal(2, result.TotalRoster);
        Assert.Equal(1, result.PresentCount);
        Assert.Equal(50d, result.AttendancePercentage);
    }

    [Fact]
    public async Task HandleAsync_WhenNoneLoaded_PresentCountZero()
    {
        var athleteId = Guid.NewGuid();
        SetupAthletes(CreateAthleteDto(athleteId, "Sofía Ruiz"));
        SetupLoads();

        var result = await CreateHandler().HandleAsync(new GetCategoryAttendanceTodayQuery(_categoryId));

        Assert.Equal(1, result.TotalRoster);
        Assert.Equal(0, result.PresentCount);
        Assert.Equal(0d, result.AttendancePercentage);
    }

    [Fact]
    public async Task HandleAsync_WhenStructureUnavailable_DegradesToTodaysLoads()
    {
        var athleteId = Guid.NewGuid();
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureUnavailableException());
        _loads
            .Setup(repo => repo.GetByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyLoad> { CreateLoad(athleteId) });

        var result = await CreateHandler().HandleAsync(new GetCategoryAttendanceTodayQuery(_categoryId));

        Assert.Equal(1, result.TotalRoster);
        Assert.Equal(1, result.PresentCount);
        Assert.Equal(100d, result.AttendancePercentage);
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryNotFound_Propagates()
    {
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CategoryNotFoundException());

        await Assert.ThrowsAsync<CategoryNotFoundException>(
            () => CreateHandler().HandleAsync(new GetCategoryAttendanceTodayQuery(_categoryId)));
    }

    [Fact]
    public async Task HandleAsync_UsesArgentinaDate_WhenQueryingLoads()
    {
        DateTime? receivedDate = null;
        SetupAthletes(CreateAthleteDto(Guid.NewGuid(), "Ana Rojas"));
        _loads
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, DateTime, CancellationToken>((_, date, _) => receivedDate = date)
            .ReturnsAsync(new List<DailyLoad>());

        await CreateHandler().HandleAsync(new GetCategoryAttendanceTodayQuery(_categoryId));

        Assert.NotNull(receivedDate);
        Assert.Equal(ArgentinaTime.Today, DateOnly.FromDateTime(receivedDate!.Value));
    }
}