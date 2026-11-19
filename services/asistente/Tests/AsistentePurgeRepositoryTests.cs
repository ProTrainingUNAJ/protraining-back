using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Asistente.Tests;

public sealed class AsistentePurgeRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AsistentePurgeRepository _repository;
    private readonly Guid _pfId = Guid.NewGuid();

    public AsistentePurgeRepositoryTests()
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

        _repository = new AsistentePurgeRepository(_context);
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

        // 1. RoutineDrafts
        var targetRoutineDraft = RoutineDraft.Create(
            targetAthleteId,
            goal: "Fuerza máxima",
            proposedTitle: "Rutina Fuerza A",
            justificationSummary: "Ajuste por perfil",
            providerUsed: "AgentOpenAi",
            exercisesJson: "[{\"name\":\"Press Banca\",\"sets\":4}]");

        var otherRoutineDraft = RoutineDraft.Create(
            otherAthleteId,
            goal: "Hipertrofia",
            proposedTitle: "Rutina Hipertrofia B",
            justificationSummary: "Volumen medio",
            providerUsed: "AgentOpenAi",
            exercisesJson: "[{\"name\":\"Sentadilla\",\"sets\":3}]");

        _context.RoutineDrafts.AddRange(targetRoutineDraft, otherRoutineDraft);

        // 2. PlanDrafts
        var targetPlanDraft = PlanDraft.Create(
            categoryId: null,
            athleteId: targetAthleteId,
            goal: "Microciclo Fuerza",
            startDate: DateOnly.FromDateTime(DateTime.UtcNow),
            content: "{\"weeks\":[]}",
            provider: "AgentOpenAi");

        var otherPlanDraft = PlanDraft.Create(
            categoryId: null,
            athleteId: otherAthleteId,
            goal: "Microciclo Resistencia",
            startDate: DateOnly.FromDateTime(DateTime.UtcNow),
            content: "{\"weeks\":[]}",
            provider: "AgentOpenAi");

        _context.PlanDrafts.AddRange(targetPlanDraft, otherPlanDraft);

        // 3. DayAdjustments
        var targetDayAdjustment = DayAdjustment.Create(
            targetAthleteId,
            date: DateOnly.FromDateTime(DateTime.UtcNow),
            sessionId: Guid.NewGuid(),
            itemsJson: "[{\"originalExercise\":\"Press militar\",\"substituteExercise\":\"Elevaciones laterales\"}]",
            justification: "Molestia en hombro detectada en check-in",
            provider: "Rules");

        var otherDayAdjustment = DayAdjustment.Create(
            otherAthleteId,
            date: DateOnly.FromDateTime(DateTime.UtcNow),
            sessionId: Guid.NewGuid(),
            itemsJson: "[{\"originalExercise\":\"Sentadilla\",\"substituteExercise\":\"Prensa\"}]",
            justification: "Dolor lumbar",
            provider: "Rules");

        _context.DayAdjustments.AddRange(targetDayAdjustment, otherDayAdjustment);

        // 4. AthleteRecommendations
        var targetRec = AthleteRecommendation.Create(
            targetAthleteId,
            title: "Reducción de volumen",
            reason: "Fatiga acumulada alta",
            recommendationText: "Bajar 1 serie por ejercicio principal.");

        var otherRec = AthleteRecommendation.Create(
            otherAthleteId,
            title: "Pausa activa",
            reason: "Semáforo amarillo recurrente",
            recommendationText: "Priorizar movilidad y descanso.");

        _context.AthleteRecommendations.AddRange(targetRec, otherRec);

        // 5. PreventiveAlerts
        var targetAlert = PreventiveAlert.Create(
            targetAthleteId,
            athleteName: "Atleta Target",
            reason: "Carga aguda > 1.5",
            suggestion: "Descanso preventivo 48hs");

        var otherAlert = PreventiveAlert.Create(
            otherAthleteId,
            athleteName: "Otro Atleta",
            reason: "Dolor persistente",
            suggestion: "Kinesiología");

        _context.PreventiveAlerts.AddRange(targetAlert, otherAlert);

        // 6. PfDecisions
        var targetDecision = PfDecision.Create(
            targetAthleteId,
            source: PfDecisionSource.DayAdjustment,
            sourceId: targetDayAdjustment.Id,
            suggestedExerciseId: Guid.NewGuid(),
            decision: PfDecisionType.Approved);

        var otherDecision = PfDecision.Create(
            otherAthleteId,
            source: PfDecisionSource.DayAdjustment,
            sourceId: otherDayAdjustment.Id,
            suggestedExerciseId: Guid.NewGuid(),
            decision: PfDecisionType.Approved);

        _context.PfDecisions.AddRange(targetDecision, otherDecision);

        // 7. AiLogs
        var targetLog = AiLog.Create(
            provider: "AgentOpenAi",
            prompt: "Generar rutina",
            latencyMs: 120,
            isValid: true,
            validationResult: "OK",
            athleteId: targetAthleteId,
            pfId: _pfId);

        var otherLog = AiLog.Create(
            provider: "AgentOpenAi",
            prompt: "Generar rutina otro",
            latencyMs: 150,
            isValid: true,
            validationResult: "OK",
            athleteId: otherAthleteId,
            pfId: _pfId);

        _context.AiLogs.AddRange(targetLog, otherLog);

        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.PurgeAthleteDataAsync(targetAthleteId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(targetAthleteId, result.AthleteId);
        Assert.Equal(1, result.RoutineDraftsCount);
        Assert.Equal(1, result.PlanDraftsCount);
        Assert.Equal(1, result.DayAdjustmentsCount);
        Assert.Equal(1, result.AthleteRecommendationsCount);
        Assert.Equal(1, result.PreventiveAlertsCount);
        Assert.Equal(1, result.PfDecisionsCount);
        Assert.Equal(1, result.AiLogsCount);
        Assert.Equal(7, result.TotalRecordsPurged);

        // Verificar que no queda ningún registro del atleta purgado en Asistente
        Assert.False(await _context.RoutineDrafts.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == targetAthleteId));
        Assert.False(await _context.PlanDrafts.IgnoreQueryFilters().AnyAsync(p => p.AthleteId == targetAthleteId));
        Assert.False(await _context.DayAdjustments.IgnoreQueryFilters().AnyAsync(d => d.AthleteId == targetAthleteId));
        Assert.False(await _context.AthleteRecommendations.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == targetAthleteId));
        Assert.False(await _context.PreventiveAlerts.IgnoreQueryFilters().AnyAsync(a => a.AthleteId == targetAthleteId));
        Assert.False(await _context.PfDecisions.IgnoreQueryFilters().AnyAsync(d => d.AthleteId == targetAthleteId));
        Assert.False(await _context.AiLogs.AnyAsync(l => l.AthleteId == targetAthleteId));

        // Verificar que los registros del otro atleta se mantienen intactos
        Assert.True(await _context.RoutineDrafts.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == otherAthleteId));
        Assert.True(await _context.PlanDrafts.IgnoreQueryFilters().AnyAsync(p => p.AthleteId == otherAthleteId));
        Assert.True(await _context.DayAdjustments.IgnoreQueryFilters().AnyAsync(d => d.AthleteId == otherAthleteId));
        Assert.True(await _context.AthleteRecommendations.IgnoreQueryFilters().AnyAsync(r => r.AthleteId == otherAthleteId));
        Assert.True(await _context.PreventiveAlerts.IgnoreQueryFilters().AnyAsync(a => a.AthleteId == otherAthleteId));
        Assert.True(await _context.PfDecisions.IgnoreQueryFilters().AnyAsync(d => d.AthleteId == otherAthleteId));
        Assert.True(await _context.AiLogs.AnyAsync(l => l.AthleteId == otherAthleteId));
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
        Assert.Equal(0, result.RoutineDraftsCount);
        Assert.Equal(0, result.PlanDraftsCount);
        Assert.Equal(0, result.DayAdjustmentsCount);
        Assert.Equal(0, result.AthleteRecommendationsCount);
        Assert.Equal(0, result.PreventiveAlertsCount);
        Assert.Equal(0, result.PfDecisionsCount);
        Assert.Equal(0, result.AiLogsCount);
        Assert.Equal(0, result.TotalRecordsPurged);
    }
}
