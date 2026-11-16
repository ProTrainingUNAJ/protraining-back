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
    private readonly Mock<IAttendanceRepository> _attendance = new();

    private GetCategoryAttendanceTodayQueryHandler CreateHandler()
    {
        _attendance
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Attendance>());
        _attendance
            .Setup(repo => repo.GetByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Attendance>());
        return new(_structure.Object, _loads.Object, _attendance.Object);
    }

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

    private void SetupAttendance(params Domain.Entities.Attendance[] corrections) =>
        _attendance
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(corrections.ToList());

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
    public async Task HandleAsync_PopulatesLoadAndActivities_WhenAthleteHasRegisteredDailyLoad()
    {
        var athleteId = Guid.NewGuid();
        var load = new DailyLoad
        {
            AthleteId = athleteId,
            Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue),
            GymMinutes = 60,
            GymRpe = 3,
            SportTrainingMinutes = 30,
            SportTrainingRpe = 4
        };

        SetupAthletes(CreateAthleteDto(athleteId, "Lucas Gómez"));
        SetupLoads(load);

        var result = await CreateHandler().HandleAsync(new GetCategoryAttendanceTodayQuery(_categoryId));

        var athlete = Assert.Single(result.Athletes);
        Assert.Equal("Present", athlete.Status);
        Assert.Equal(90, athlete.TotalMinutes);
        Assert.Equal(60 * 3 + 30 * 4, athlete.TotalLoad);
        Assert.NotNull(athlete.Activities);
        Assert.Equal(2, athlete.Activities.Count);
        Assert.Contains(athlete.Activities, a => a.Type == "Gym" && a.Minutes == 60 && a.Rpe == 3);
        Assert.Contains(athlete.Activities, a => a.Type == "SportTraining" && a.Minutes == 30 && a.Rpe == 4);
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