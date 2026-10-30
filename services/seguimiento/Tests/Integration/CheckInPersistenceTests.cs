using System.Diagnostics;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Seguimiento.Tests.Integration;

public sealed class CheckInPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    public CheckInPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ICheckInRepository, CheckInRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<TrafficLightEngine>();
        services.AddScoped<SubmitCheckInCommandHandler>();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.PfId).Returns(_pfId);
        services.AddSingleton(currentUser.Object);

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private SubmitCheckInCommandHandler CreateHandler() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<SubmitCheckInCommandHandler>();

    private AppDbContext CreateDbContext() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    [Fact]
    public async Task Given_ValidCheckIn_When_Submitted_Then_PersistsAndCompletesUnder30Seconds()
    {
        var stopwatch = Stopwatch.StartNew();
        var (response, created) = await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(_athleteId, 8, 4, 1, 2, 4, 2, false, 4, null, false));
        stopwatch.Stop();

        Assert.True(created);
        Assert.True(stopwatch.ElapsedMilliseconds < 30_000,
            $"El check-in demoró {stopwatch.ElapsedMilliseconds} ms (límite 30000 ms).");

        using var db = CreateDbContext();
        var saved = await db.CheckIns.IgnoreQueryFilters().SingleAsync(checkIn => checkIn.AthleteId == _athleteId);
        Assert.Equal(8, saved.SleepHours);
        Assert.Null(saved.PainZones);
        Assert.Equal(TrafficLight.Green, saved.TrafficLight);
        Assert.Equal("Estado óptimo", saved.TrafficLightReason);
        Assert.Equal(_pfId, saved.PfId);
    }

    [Fact]
    public async Task Given_SameAthleteAndDay_When_SubmittedTwice_Then_UpsertsSingleRow()
    {
        await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(_athleteId, 8, 4, 1, 2, 4, 2, false, 4, null, false));
        await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(_athleteId, 5, 3, 2, 3, 3, 3, true, 6, "tobillo", true));

        using var db = CreateDbContext();
        var rows = await db.CheckIns.IgnoreQueryFilters()
            .Where(checkIn => checkIn.AthleteId == _athleteId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(5, rows[0].SleepHours);
        Assert.True(rows[0].HasActiveInjury);
        Assert.Equal(TrafficLight.Red, rows[0].TrafficLight);
    }

    [Fact]
    public async Task Given_InvalidSleepHours_When_Submitted_Then_ThrowsAndDoesNotPersist()
    {
        await Assert.ThrowsAsync<DomainException>(() => CreateHandler().HandleAsync(
            new SubmitCheckInCommand(_athleteId, 30, 4, 1, 2, 4, 2, false, 4, null, false)));

        using var db = CreateDbContext();
        Assert.Empty(await db.CheckIns.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Given_OfflineCheckInFromYesterday_When_SyncedNextDay_Then_KeepsOriginalDate()
    {
        var yesterday = ArgentinaTime.Today.AddDays(-1);

        await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(_athleteId, 8, 4, 1, 2, 4, 2, false, 4, null, false, Date: yesterday));
        await CreateHandler().HandleAsync(
            new SubmitCheckInCommand(_athleteId, 7, 3, 2, 3, 3, 3, true, 6, "tobillo", true));

        using var db = CreateDbContext();
        var rows = await db.CheckIns.IgnoreQueryFilters()
            .Where(checkIn => checkIn.AthleteId == _athleteId)
            .OrderBy(checkIn => checkIn.Date)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(yesterday, DateOnly.FromDateTime(rows[0].Date));
        Assert.Equal(ArgentinaTime.Today, DateOnly.FromDateTime(rows[1].Date));
    }

    [Fact]
    public async Task Given_DuplicateAthleteAndDate_When_InsertedDirectly_Then_ThrowsDbUpdateException()
    {
        var date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);

        using (var db = CreateDbContext())
        {
            db.CheckIns.Add(new CheckIn
            {
                AthleteId = _athleteId,
                Date = date,
                TrafficLight = TrafficLight.Green,
                SleepHours = 8,
                SleepQuality = 4,
                MuscleSoreness = 1,
                FatigueLevel = 2,
                MoodRate = 4,
                StressLevel = 2,
                IsSick = false,
                EffortRate = 4,
                HasActiveInjury = false
            });
            await db.SaveChangesAsync();
        }

        using (var db = CreateDbContext())
        {
            db.CheckIns.Add(new CheckIn
            {
                AthleteId = _athleteId,
                Date = date,
                TrafficLight = TrafficLight.Green,
                SleepHours = 7,
                SleepQuality = 3,
                MuscleSoreness = 2,
                FatigueLevel = 3,
                MoodRate = 3,
                StressLevel = 3,
                IsSick = false,
                EffortRate = 5,
                HasActiveInjury = false
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}