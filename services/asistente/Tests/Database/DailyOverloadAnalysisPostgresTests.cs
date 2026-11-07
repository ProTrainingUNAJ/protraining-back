using API.Services;
using Application.DTOs.Alerts;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

namespace Asistente.Tests.Database;

[Trait("Category", "RequiresDocker")]
[Trait("Category", "Isolation")]
public sealed class DailyOverloadAnalysisPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();
    private readonly Guid _athleteAId = Guid.NewGuid();
    private readonly Guid _athleteBId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext(CreateCurrentUser());
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task HandleAsync_WhenRunTwice_PersistsEachAlertWithItsPfAndDoesNotDuplicate()
    {
        var currentUser = CreateCurrentUser();
        await using var context = CreateContext(currentUser);
        var handler = CreateHandler(context, currentUser);

        var firstResult = await handler.HandleAsync(
            new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 7)));

        // Limpiar el change tracker para simular una segunda corrida independiente (nuevo scope/proceso)
        context.ChangeTracker.Clear();

        var secondResult = await handler.HandleAsync(
            new ProcessDailyOverloadAnalysisCommand(new DateOnly(2026, 11, 7)));

        Assert.Equal(2, firstResult.GeneratedAlerts);
        Assert.Equal(0, secondResult.GeneratedAlerts);
        Assert.Equal(2, secondResult.SkippedAlerts);

        var persistedAlerts = await context.PreventiveAlerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(2, persistedAlerts.Count);

        var alertA = Assert.Single(persistedAlerts, alert => alert.AthleteId == _athleteAId);
        Assert.Equal(_pfAId, alertA.PfId);

        var alertB = Assert.Single(persistedAlerts, alert => alert.AthleteId == _athleteBId);
        Assert.Equal(_pfBId, alertB.PfId);

        Assert.Equal(2, persistedAlerts.Select(alert => alert.AthleteId).Distinct().Count());
    }

    private ProcessDailyOverloadAnalysisCommandHandler CreateHandler(
        AppDbContext context,
        ICurrentUser currentUser)
    {
        var structure = new Mock<IStructureAthleteClient>();
        structure.Setup(client => client.GetAthletesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new StructureAthleteSummaryDto(_athleteAId, "Atleta A", null, true, _pfAId),
                new StructureAthleteSummaryDto(_athleteBId, "Atleta B", null, true, _pfBId)
            ]);

        var seguimiento = new Mock<ISeguimientoClient>();
        seguimiento.Setup(client => client.GetAthleteCheckInsHistoryAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateOverloadCheckIn(_athleteAId),
                CreateOverloadCheckIn(_athleteBId)
            ]);

        var calendar = new Mock<IStructureCalendarClient>();
        calendar.Setup(client => client.GetMatchDatesAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new ProcessDailyOverloadAnalysisCommandHandler(
            structure.Object,
            seguimiento.Object,
            calendar.Object,
            new PreventiveAlertRepository(context),
            new UnitOfWork(context),
            currentUser,
            NullLogger<ProcessDailyOverloadAnalysisCommandHandler>.Instance);
    }

    private AppDbContext CreateContext(ICurrentUser currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, currentUser);
    }

    private static ICurrentUser CreateCurrentUser() =>
        new CurrentUser(new HttpContextAccessor());

    private static AthleteCheckInHistoryItemDto CreateOverloadCheckIn(Guid athleteId) =>
        new(
            Guid.NewGuid(),
            athleteId,
            new DateOnly(2026, 11, 7),
            5,
            2,
            4,
            3,
            3,
            3,
            9,
            false,
            null,
            false);
}
