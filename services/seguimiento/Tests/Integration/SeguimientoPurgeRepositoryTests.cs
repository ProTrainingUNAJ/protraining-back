using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Seguimiento.Tests.Integration;

public sealed class SeguimientoPurgeRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly SeguimientoPurgeRepository _repository;
    private readonly Guid _pfId = Guid.NewGuid();

    public SeguimientoPurgeRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.PfId).Returns(_pfId);

        _context = new AppDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        _repository = new SeguimientoPurgeRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Given_AthleteWithDataAcrossAllTables_When_PurgingAthlete_Then_DeletesAllDataAndPreservesOtherAthletes()
    {
        // Arrange
        var targetAthleteId = Guid.NewGuid();
        var otherAthleteId = Guid.NewGuid();

        // 1. Check-ins
        var targetCheckIn1 = new CheckIn
        {
            AthleteId = targetAthleteId,
            Date = DateTime.UtcNow.Date.AddDays(-1),
            TrafficLight = TrafficLight.Green,
            SleepHours = 8,
            SleepQuality = 4,
            MuscleSoreness = 1,
            FatigueLevel = 1,
            MoodRate = 5,
            StressLevel = 1
        };
        var targetCheckIn2 = new CheckIn
        {
            AthleteId = targetAthleteId,
            Date = DateTime.UtcNow.Date,
            TrafficLight = TrafficLight.Yellow,
            SleepHours = 7,
            SleepQuality = 3,
            MuscleSoreness = 2,
            FatigueLevel = 2,
            MoodRate = 4,
            StressLevel = 2
        };
        var otherCheckIn = new CheckIn
        {
            AthleteId = otherAthleteId,
            Date = DateTime.UtcNow.Date,
            TrafficLight = TrafficLight.Green,
            SleepHours = 8,
            SleepQuality = 5,
            MuscleSoreness = 1,
            FatigueLevel = 1,
            MoodRate = 5,
            StressLevel = 1
        };
        _context.CheckIns.AddRange(targetCheckIn1, targetCheckIn2, otherCheckIn);

        // 2. Lesiones
        var targetInjury = AthleteInjury.Create(
            targetAthleteId,
            "isquiotibiales",
            "Sobrecarga muscular",
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(7));

        var otherInjury = AthleteInjury.Create(
            otherAthleteId,
            "cuadriceps",
            "Contractura leve",
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(5));

        _context.AthleteInjuries.AddRange(targetInjury, otherInjury);

        // 3. Detalles de dolor en check-in asociados
        var targetPain = new CheckInInjuryPain
        {
            CheckInId = targetCheckIn2.Id,
            InjuryId = targetInjury.Id,
            Level = 3
        };
        _context.CheckInInjuryPains.Add(targetPain);

        // 4. Cambios de estado de lesión
        var statusChange = new AthleteInjuryStatusChange
        {
            InjuryId = targetInjury.Id,
            FromStatus = InjuryStatus.Active,
            ToStatus = InjuryStatus.InRecovery,
            Note = "Inicio de kinesiología",
            ChangedAt = DateTime.UtcNow,
            ChangedBy = _pfId
        };
        _context.AthleteInjuryStatusChanges.Add(statusChange);

        // 5. Cargas diarias
        var targetLoad = DailyLoad.Register(
            targetAthleteId,
            DateTime.UtcNow.Date,
            gymMinutes: 45, gymRpe: 7,
            sportTrainingMinutes: 60, sportTrainingRpe: 8,
            match1Minutes: 0, match1Rpe: null,
            match2Minutes: 0, match2Rpe: null,
            individualWorkMinutes: 15, individualWorkRpe: 5);

        var otherLoad = DailyLoad.Register(
            otherAthleteId,
            DateTime.UtcNow.Date,
            gymMinutes: 30, gymRpe: 6,
            sportTrainingMinutes: 45, sportTrainingRpe: 7,
            match1Minutes: 0, match1Rpe: null,
            match2Minutes: 0, match2Rpe: null,
            individualWorkMinutes: 10, individualWorkRpe: 4);

        _context.DailyLoads.AddRange(targetLoad, otherLoad);

        // 6. Resultados de tests de campo
        var targetTestResult = new FieldTestResult
        {
            SessionId = Guid.NewGuid(),
            CatalogTestId = Guid.NewGuid(),
            AthleteId = targetAthleteId,
            Value = 85.5m,
            Attempt = 1,
            RecordedAt = DateTime.UtcNow
        };
        var otherTestResult = new FieldTestResult
        {
            SessionId = Guid.NewGuid(),
            CatalogTestId = Guid.NewGuid(),
            AthleteId = otherAthleteId,
            Value = 90.0m,
            Attempt = 1,
            RecordedAt = DateTime.UtcNow
        };
        _context.FieldTestResults.AddRange(targetTestResult, otherTestResult);

        // 7. Asistencias
        var targetAttendance = new Attendance
        {
            AthleteId = targetAthleteId,
            Date = DateTime.UtcNow.Date,
            IsPresent = true
        };
        var otherAttendance = new Attendance
        {
            AthleteId = otherAthleteId,
            Date = DateTime.UtcNow.Date,
            IsPresent = true
        };
        _context.Attendances.AddRange(targetAttendance, otherAttendance);

        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(targetAthleteId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(targetAthleteId, result.AthleteId);
        Assert.Equal(2, result.CheckInsCount);
        Assert.Equal(1, result.InjuriesCount);
        Assert.Equal(1, result.DailyLoadsCount);
        Assert.Equal(1, result.FieldTestResultsCount);
        Assert.Equal(1, result.AttendancesCount);
        Assert.Equal(1, result.CheckInInjuryPainsCount);
        Assert.Equal(1, result.InjuryStatusChangesCount);
        Assert.Equal(8, result.TotalRecordsPurged);

        // Verificar que en la base de datos no queda nada del atleta purgado
        Assert.False(await _context.CheckIns.IgnoreQueryFilters().AnyAsync(c => c.AthleteId == targetAthleteId));
        Assert.False(await _context.AthleteInjuries.IgnoreQueryFilters().AnyAsync(i => i.AthleteId == targetAthleteId));
        Assert.False(await _context.CheckInInjuryPains.IgnoreQueryFilters().AnyAsync(p => p.CheckInId == targetCheckIn1.Id || p.CheckInId == targetCheckIn2.Id || p.InjuryId == targetInjury.Id));
        Assert.False(await _context.AthleteInjuryStatusChanges.IgnoreQueryFilters().AnyAsync(c => c.InjuryId == targetInjury.Id));
        Assert.False(await _context.DailyLoads.IgnoreQueryFilters().AnyAsync(d => d.AthleteId == targetAthleteId));
        Assert.False(await _context.FieldTestResults.IgnoreQueryFilters().AnyAsync(f => f.AthleteId == targetAthleteId));
        Assert.False(await _context.Attendances.IgnoreQueryFilters().AnyAsync(a => a.AthleteId == targetAthleteId));

        // Verificar que los datos del otro atleta permanecen intactos
        Assert.True(await _context.CheckIns.IgnoreQueryFilters().AnyAsync(c => c.AthleteId == otherAthleteId));
        Assert.True(await _context.AthleteInjuries.IgnoreQueryFilters().AnyAsync(i => i.AthleteId == otherAthleteId));
        Assert.True(await _context.DailyLoads.IgnoreQueryFilters().AnyAsync(d => d.AthleteId == otherAthleteId));
        Assert.True(await _context.FieldTestResults.IgnoreQueryFilters().AnyAsync(f => f.AthleteId == otherAthleteId));
        Assert.True(await _context.Attendances.IgnoreQueryFilters().AnyAsync(a => a.AthleteId == otherAthleteId));
    }

    [Fact]
    public async Task Given_PurgedAthlete_When_PurgingAgain_Then_ReturnsZeroAndDoesNotThrow()
    {
        // Arrange
        var athleteId = Guid.NewGuid();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(athleteId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(athleteId, result.AthleteId);
        Assert.Equal(0, result.CheckInsCount);
        Assert.Equal(0, result.InjuriesCount);
        Assert.Equal(0, result.DailyLoadsCount);
        Assert.Equal(0, result.FieldTestResultsCount);
        Assert.Equal(0, result.AttendancesCount);
        Assert.Equal(0, result.TotalRecordsPurged);
    }
}
