namespace Asistente.Tests.Database;

using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// Pruebas de integración con PostgreSQL real (Testcontainers) para PfDecisionRepository (#128 · KAN-423).
/// Verifica directamente contra base de datos real:
/// 1. El tope estricto de 50 registros (insertando 60 en la base para verificar que el Take(50) no sea falseado por un mock).
/// 2. El orden descendente por Date y CreatedAt.
/// 3. El filtro exacto por zona anatómica (?zone=).
/// 4. El aislamiento multi-tenant por PfId.
/// </summary>
[Trait("Category", "RequiresDocker")]
[Trait("Category", "Isolation")]
public sealed class PfDecisionRepositoryPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext(_pfAId);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AppDbContext CreateContext(Guid pfId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.PfId).Returns(pfId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        return new AppDbContext(options, currentUser.Object);
    }

    [Fact]
    public async Task Given_60DecisionsInRealPostgres_When_QueryingWithoutZone_Then_LimitsTo50AndOrdersDescendingByDate()
    {
        // Arrange: Insertar 60 decisiones reales en Postgres para el atleta con fechas secuenciales
        await using var context = CreateContext(_pfAId);
        var repository = new PfDecisionRepository(context);

        var baseDate = new DateOnly(2026, 1, 1);
        for (var i = 1; i <= 60; i++)
        {
            var decision = PfDecision.Create(
                athleteId: _athleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: Guid.NewGuid(),
                suggestedExerciseId: _exerciseId,
                decision: PfDecisionType.Approved,
                zone: i <= 40 ? "rodilla" : "espalda_baja",
                date: baseDate.AddDays(i));

            await repository.AddAsync(decision);
        }
        await context.SaveChangesAsync();

        // Act: Consultar sin filtro de zona
        await using var queryContext = CreateContext(_pfAId);
        var queryRepo = new PfDecisionRepository(queryContext);
        var result = await queryRepo.GetRecentByAthleteAsync(_athleteId, zone: null, limit: 50);

        // Assert
        // 1. Debe limitar exactamente a 50 (probando que el Take(50) de EF Core en Postgres funciona de verdad)
        Assert.Equal(50, result.Count);

        // 2. Debe estar ordenado descendentemente por Date (el primero debe ser el día 60)
        Assert.Equal(baseDate.AddDays(60), result[0].Date);
        Assert.Equal(baseDate.AddDays(11), result[49].Date);

        for (var k = 0; k < result.Count - 1; k++)
        {
            Assert.True(result[k].Date >= result[k + 1].Date,
                $"El elemento en {k} ({result[k].Date}) no es posterior o igual al elemento {k + 1} ({result[k + 1].Date}).");
        }
    }

    [Fact]
    public async Task Given_DecisionsInRealPostgres_When_FilteringByZone_Then_ReturnsOnlyDecisionsForThatZone()
    {
        // Arrange
        await using var context = CreateContext(_pfAId);
        var repository = new PfDecisionRepository(context);

        var baseDate = new DateOnly(2026, 2, 1);
        for (var i = 1; i <= 30; i++)
        {
            var decision = PfDecision.Create(
                athleteId: _athleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: Guid.NewGuid(),
                suggestedExerciseId: _exerciseId,
                decision: PfDecisionType.Modified,
                finalExerciseId: Guid.NewGuid(),
                zone: i <= 10 ? "hombro" : "espalda_baja",
                date: baseDate.AddDays(i));

            await repository.AddAsync(decision);
        }
        await context.SaveChangesAsync();

        // Act: Consultar filtrando por "hombro"
        await using var queryContext = CreateContext(_pfAId);
        var queryRepo = new PfDecisionRepository(queryContext);
        var hombroResults = await queryRepo.GetRecentByAthleteAsync(_athleteId, zone: "hombro", limit: 50);
        var lumbarResults = await queryRepo.GetRecentByAthleteAsync(_athleteId, zone: "espalda_baja", limit: 50);

        // Assert
        Assert.Equal(10, hombroResults.Count);
        Assert.All(hombroResults, d => Assert.Equal("hombro", d.Zone));

        Assert.Equal(20, lumbarResults.Count);
        Assert.All(lumbarResults, d => Assert.Equal("espalda_baja", d.Zone));
    }

    [Fact]
    public async Task Given_DecisionsOfPfA_When_PfBQueries_Then_ReturnsEmptyDueToTenantFilter()
    {
        // Arrange
        await using var contextA = CreateContext(_pfAId);
        var repoA = new PfDecisionRepository(contextA);

        var decision = PfDecision.Create(
            athleteId: _athleteId,
            source: PfDecisionSource.DayAdjustment,
            sourceId: Guid.NewGuid(),
            suggestedExerciseId: _exerciseId,
            decision: PfDecisionType.Approved,
            zone: "rodilla",
            date: new DateOnly(2026, 3, 1));

        await repoA.AddAsync(decision);
        await contextA.SaveChangesAsync();

        // Act: PF-B consulta las decisiones del mismo atleta
        await using var contextB = CreateContext(_pfBId);
        var repoB = new PfDecisionRepository(contextB);
        var resultPfB = await repoB.GetRecentByAthleteAsync(_athleteId);

        // Assert: El filtro global de BaseEntity por PfId previene que PF-B vea los registros de PF-A
        Assert.Empty(resultPfB);
    }
}

