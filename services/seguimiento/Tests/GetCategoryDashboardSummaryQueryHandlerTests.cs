using Application.DTOs.Injuries;
using Application.DTOs.Structure;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Dashboard.GetCategoryDashboardSummary;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Moq;

namespace Seguimiento.Tests;

public class GetCategoryDashboardSummaryQueryHandlerTests
{
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly DateOnly _from = new(2026, 11, 2);
    private readonly DateOnly _to = new(2026, 11, 4);
    private readonly Mock<IStructureClient> _structure = new();
    private readonly Mock<IAttendanceRepository> _attendance = new();
    private readonly Mock<IDailyLoadRepository> _loads = new();
    private readonly Mock<IAthleteInjuryRepository> _injuries = new();

    private GetCategoryDashboardSummaryQueryHandler CreateHandler() => new(
        _structure.Object,
        _attendance.Object,
        _loads.Object,
        _injuries.Object);

    private void SetupOwnCategory(params Guid[] athleteIds)
    {
        _structure
            .Setup(client => client.IsOwnCategoryAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athleteIds
                .Select(id => new CategoryAthleteDto { Id = id, FullName = "Atleta" })
                .ToList());
        _attendance
            .Setup(repo => repo.GetByAthletesAndDateRangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Attendance>());
        _loads
            .Setup(repo => repo.GetByAthletesAndDateRangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyLoad>());
        _injuries
            .Setup(repo => repo.GetPeriodAggregateByAthleteIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InjuryPeriodAggregateDto(0, 0, 0, 0));
    }

    private static DailyLoad Load(Guid athleteId, DateOnly date, int minutes, int rpe) => new()
    {
        AthleteId = athleteId,
        Date = date.ToDateTime(TimeOnly.MinValue),
        GymMinutes = minutes,
        GymRpe = rpe
    };

    private static Domain.Entities.Attendance Correction(Guid athleteId, DateOnly date, bool present) => new()
    {
        AthleteId = athleteId,
        Date = date.ToDateTime(TimeOnly.MinValue),
        IsPresent = present
    };

    [Fact]
    public async Task HandleAsync_AggregatesAttendanceInjuriesAndLoad()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        SetupOwnCategory(first, second);

        _loads
            .Setup(repo => repo.GetByAthletesAndDateRangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyLoad>
            {
                Load(first, _from, 60, 3),
                Load(second, _from.AddDays(1), 30, 4)
            });
        _attendance
            .Setup(repo => repo.GetByAthletesAndDateRangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Attendance>
            {
                Correction(first, _from.AddDays(1), false)
            });
        _injuries
            .Setup(repo => repo.GetPeriodAggregateByAthleteIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InjuryPeriodAggregateDto(1, 1, 1, 2));

        var result = await CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, _from, _to));

        Assert.Equal(2, result.AthleteCount);
        Assert.Equal(2, result.EvaluatedDays);
        Assert.Equal(2, result.Attendance.PresentDays);
        Assert.Equal(1, result.Attendance.AbsentDays);
        Assert.Equal(1, result.Attendance.WithoutRecordDays);
        Assert.Equal(4, result.Attendance.EvaluatedAthleteDays);
        Assert.Equal(50d, result.Attendance.AttendancePercentage);
        Assert.Equal(1, result.Injuries.ActiveCount);
        Assert.Equal(1, result.Injuries.InRecoveryCount);
        Assert.Equal(1, result.Injuries.ClearedInPeriodCount);
        Assert.Equal(2, result.Injuries.AthletesAffected);
        Assert.Equal(300, result.Load.TotalLoad);
        Assert.Equal(90, result.Load.TotalMinutes);
        Assert.Equal(2, result.Load.RecordsCount);
        Assert.Single(result.Load.Weeks);
        Assert.Equal(300, result.Load.Weeks[0].TotalLoad);
    }

    [Fact]
    public async Task HandleAsync_CorrectionOverridesLoad()
    {
        var athlete = Guid.NewGuid();
        SetupOwnCategory(athlete);
        _loads
            .Setup(repo => repo.GetByAthletesAndDateRangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyLoad> { Load(athlete, _from, 60, 3) });
        _attendance
            .Setup(repo => repo.GetByAthletesAndDateRangeAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Attendance> { Correction(athlete, _from, false) });

        var result = await CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, _from, _to));

        Assert.Equal(0, result.Attendance.PresentDays);
        Assert.Equal(1, result.Attendance.AbsentDays);
        Assert.Equal(0d, result.Attendance.AttendancePercentage);
    }

    [Fact]
    public async Task HandleAsync_WhenNoAthletes_ReturnsZerosAndKeepsInjuryCounts()
    {
        SetupOwnCategory();
        _injuries
            .Setup(repo => repo.GetPeriodAggregateByAthleteIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InjuryPeriodAggregateDto(1, 0, 0, 1));

        var result = await CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, _from, _to));

        Assert.Equal(0, result.AthleteCount);
        Assert.Equal(0, result.Attendance.EvaluatedAthleteDays);
        Assert.Equal(0, result.Load.TotalLoad);
        Assert.Empty(result.Load.Weeks);
        Assert.Equal(1, result.Injuries.ActiveCount);
    }

    [Fact]
    public async Task HandleAsync_WithoutRecords_ReturnsZeroPercentage()
    {
        SetupOwnCategory(Guid.NewGuid());

        var result = await CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, _from, _to));

        Assert.Equal(0, result.EvaluatedDays);
        Assert.Equal(0, result.Attendance.PresentDays);
        Assert.Equal(0d, result.Attendance.AttendancePercentage);
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryBelongsToAnotherPf_ThrowsCategoryNotFound()
    {
        _structure
            .Setup(client => client.IsOwnCategoryAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<CategoryNotFoundException>(() => CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, _from, _to)));

        _attendance.VerifyNoOtherCalls();
        _loads.VerifyNoOtherCalls();
        _injuries.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenRangeIsInverted_ThrowsDomainException()
    {
        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, _to, _from)));
    }

    [Fact]
    public async Task HandleAsync_WhenRangeExceedsMaxDays_ThrowsDomainException()
    {
        var from = _to.AddDays(-(WeeklyTrendsCalculator.MaxRangeDays + 1));

        var exception = await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new GetCategoryDashboardSummaryQuery(_categoryId, from, _to)));

        Assert.Contains("180", exception.Message);
        _structure.VerifyNoOtherCalls();
    }
}
