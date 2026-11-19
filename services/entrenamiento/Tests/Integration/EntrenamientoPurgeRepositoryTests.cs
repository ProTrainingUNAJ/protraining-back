using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Entrenamiento.Tests.Integration;

public sealed class EntrenamientoPurgeRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly EntrenamientoPurgeRepository _repository;
    private readonly Guid _pfId = Guid.NewGuid();

    public EntrenamientoPurgeRepositoryTests()
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

        _repository = new EntrenamientoPurgeRepository(_context);
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

        var exercise = Exercise.Create("Press banca", "Pecho");
        _context.Exercises.Add(exercise);

        var plan = TrainingPlan.Create("Plan Purga Test");
        var week = TrainingWeek.Create(1, plan.Id);
        var session = TrainingSession.Create(week.Id, "Sesión 1", 1, DayOfWeek.Monday);
        week.AddSession(session);
        plan.AddWeek(week);
        _context.TrainingPlans.Add(plan);

        await _context.SaveChangesAsync();

        var exerciseId = exercise.Id;
        var sessionId = session.Id;
        var planId = plan.Id;

        // 1. Series ejecutadas
        var targetSet = PerformedSet.Create(
            targetAthleteId, sessionId, exerciseId, DateOnly.FromDateTime(DateTime.UtcNow), weight: 100m, reps: 5, timeSeconds: null, setNumber: 1);
        var otherSet = PerformedSet.Create(
            otherAthleteId, sessionId, exerciseId, DateOnly.FromDateTime(DateTime.UtcNow), weight: 90m, reps: 5, timeSeconds: null, setNumber: 1);
        _context.PerformedSets.AddRange(targetSet, otherSet);

        // 2. Propuestas de récord
        var targetProposal = RecordProposal.Create(
            targetAthleteId, exerciseId, currentWeight: 100m, proposedWeight: 105m, performedWeight: 105m, performedReps: 5);
        var otherProposal = RecordProposal.Create(
            otherAthleteId, exerciseId, currentWeight: 90m, proposedWeight: 95m, performedWeight: 95m, performedReps: 5);
        _context.RecordProposals.AddRange(targetProposal, otherProposal);

        // 3. Récords vigentes
        var targetRecord = AthleteRecord.Create(
            targetAthleteId, exerciseId, weight: 100m, RecordSource.Tested, DateTime.UtcNow);
        var otherRecord = AthleteRecord.Create(
            otherAthleteId, exerciseId, weight: 90m, RecordSource.Tested, DateTime.UtcNow);
        _context.AthleteRecords.AddRange(targetRecord, otherRecord);

        // 4. Ajustes de sesión
        var targetAdjustment = AthleteSessionAdjustment.Create(
            sessionId, targetAthleteId, DateOnly.FromDateTime(DateTime.UtcNow), itemsJson: "[]", note: "Reducción de carga", trainingPlanId: planId, loadReductionPercentage: 15);
        var otherAdjustment = AthleteSessionAdjustment.Create(
            sessionId, otherAthleteId, DateOnly.FromDateTime(DateTime.UtcNow), itemsJson: "[]", note: "Reducción de carga", trainingPlanId: planId, loadReductionPercentage: 10);
        _context.AthleteSessionAdjustments.AddRange(targetAdjustment, otherAdjustment);

        // 5. Asignaciones de plan
        var targetAssignment = PlanAssignment.Create(
            planId, DateOnly.FromDateTime(DateTime.UtcNow), categoryId: null, athleteId: targetAthleteId);
        var otherAssignment = PlanAssignment.Create(
            planId, DateOnly.FromDateTime(DateTime.UtcNow), categoryId: null, athleteId: otherAthleteId);
        _context.PlanAssignments.AddRange(targetAssignment, otherAssignment);

        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(targetAthleteId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(targetAthleteId, result.AthleteId);
        Assert.Equal(1, result.PerformedSetsCount);
        Assert.Equal(1, result.RecordProposalsCount);
        Assert.Equal(1, result.AthleteRecordsCount);
        Assert.Equal(1, result.SessionAdjustmentsCount);
        Assert.Equal(1, result.PlanAssignmentsCount);
        Assert.Equal(5, result.TotalRecordsPurged);

        // Verificar que en la base de datos no queda nada del atleta purgado
        Assert.False(await _context.PerformedSets.IgnoreQueryFilters().AnyAsync(p => p.AthleteId == targetAthleteId));
        Assert.False(await _context.RecordProposals.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == targetAthleteId));
        Assert.False(await _context.AthleteRecords.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == targetAthleteId));
        Assert.False(await _context.AthleteSessionAdjustments.IgnoreQueryFilters().AnyAsync(a => a.AthleteId == targetAthleteId));
        Assert.False(await _context.PlanAssignments.IgnoreQueryFilters().AnyAsync(p => p.AthleteId == targetAthleteId));

        // Verificar que los datos del otro atleta permanecen intactos
        Assert.True(await _context.PerformedSets.IgnoreQueryFilters().AnyAsync(p => p.AthleteId == otherAthleteId));
        Assert.True(await _context.RecordProposals.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == otherAthleteId));
        Assert.True(await _context.AthleteRecords.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == otherAthleteId));
        Assert.True(await _context.AthleteSessionAdjustments.IgnoreQueryFilters().AnyAsync(a => a.AthleteId == otherAthleteId));
        Assert.True(await _context.PlanAssignments.IgnoreQueryFilters().AnyAsync(p => p.AthleteId == otherAthleteId));
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
        Assert.Equal(0, result.PerformedSetsCount);
        Assert.Equal(0, result.RecordProposalsCount);
        Assert.Equal(0, result.AthleteRecordsCount);
        Assert.Equal(0, result.SessionAdjustmentsCount);
        Assert.Equal(0, result.PlanAssignmentsCount);
        Assert.Equal(0, result.TotalRecordsPurged);
    }
}
