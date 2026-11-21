using Domain.Common;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;
using Testcontainers.PostgreSql;

namespace Seguimiento.Tests.Database;

/// <summary>
/// KAN-504: ClearedAt es timestamptz. Con Npgsql real, un DateTime con Kind=Unspecified
/// (lo que sale de DateOnly.ToDateTime) revienta la consulta. Estos tests fijan que el
/// agregado use los límites UTC derivados de la hora Argentina y devuelva solo conteos.
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class CategoryDashboardInjuryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options,
            new TestCurrentUser(_pfId, role: "PreparadorFisico"));

    [Fact]
    public async Task GetPeriodAggregateByAthleteIdsAsync_WithClearedAtInRange_UsesUtcBoundsAndCountsOnlyAggregates()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        var clearedAt = ArgentinaTime.ToUtcStartOfDay(new DateOnly(2026, 11, 15)).AddHours(3);

        await using (var seed = CreateContext())
        {
            seed.AthleteInjuries.Add(new AthleteInjury
            {
                AthleteId = _athleteId,
                Zone = "isquiotibiales",
                Description = "Desgarro leve",
                InjuryDate = new DateTime(2026, 11, 1),
                EstimatedReturnDate = new DateTime(2026, 11, 30),
                Status = InjuryStatus.MedicallyCleared,
                ClearedAt = clearedAt
            });
            seed.AthleteInjuries.Add(new AthleteInjury
            {
                AthleteId = _athleteId,
                Zone = "gemelos",
                Description = "Contractura",
                InjuryDate = new DateTime(2026, 11, 2),
                EstimatedReturnDate = new DateTime(2026, 11, 20),
                Status = InjuryStatus.Active
            });
            await seed.SaveChangesAsync();
        }

        await using var queryContext = CreateContext();
        var repository = new AthleteInjuryRepository(queryContext);

        var result = await repository.GetPeriodAggregateByAthleteIdsAsync(new[] { _athleteId }, from, to);

        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(0, result.InRecoveryCount);
        Assert.Equal(1, result.ClearedInPeriodCount);
        Assert.Equal(1, result.AthletesAffected);
    }
}
