using System.Net;
using System.Text;
using Application.Interfaces.Services;
using Application.Services;
using Application.UseCases.Trends.GetAthleteWeeklyTrends;
using Application.UseCases.Trends.GetCategoryWeeklyTrends;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Seguimiento.Tests.Infrastructure;
using Testcontainers.PostgreSql;

namespace Seguimiento.Tests.Database;

/// <summary>
/// Validación de tendencias semanales KAN-489 contra PostgreSQL 16 real (Testcontainers):
/// migraciones EF Core, filtro multitenant por pf_id, soft delete, agregación en lote y StructureClient HTTP.
/// </summary>
[Trait("Category", "RequiresDocker")]
[Trait("Category", "Isolation")]
public sealed class WeeklyTrendsPostgresTests : IAsyncLifetime
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubServiceTokenFactory : IServiceTokenFactory
    {
        public string CreateToken() => "service-token-test";
    }

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext(new TestCurrentUser(Guid.NewGuid()));
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task KAN489_10_PostgresReal_ComputesFourWeeksGroupTrends_RespectsSoftDeleteAndPfIsolation()
    {
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var athlete1 = Guid.NewGuid();
        var athlete2 = Guid.NewGuid();
        var foreignAthlete = Guid.NewGuid();

        var fixedClock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero));

        // Seed en PostgreSQL real bajo PF A y PF B.
        await using (var seedA = CreateContext(new TestCurrentUser(pfA, role: "PreparadorFisico")))
        {
            // Semana 1 (2026-10-26..11-01): solo Atleta 1
            seedA.CheckIns.Add(BuildCheckIn(athlete1, new DateOnly(2026, 10, 27), 4, 4, 2, 2, 2)); // 3.6
            seedA.DailyLoads.Add(BuildLoad(athlete1, new DateOnly(2026, 10, 28), 60, 5));         // 300

            // Semana 2 (2026-11-02..11-08): Atleta 1 (wellness 5.0, 2 check-ins) y Atleta 2 (wellness 1.0, 1 check-in)
            seedA.CheckIns.Add(BuildCheckIn(athlete1, new DateOnly(2026, 11, 3), 5, 5, 1, 1, 1));
            seedA.CheckIns.Add(BuildCheckIn(athlete1, new DateOnly(2026, 11, 4), 5, 5, 1, 1, 1));
            seedA.CheckIns.Add(BuildCheckIn(athlete2, new DateOnly(2026, 11, 5), 1, 1, 5, 5, 5));
            seedA.DailyLoads.Add(BuildLoad(athlete1, new DateOnly(2026, 11, 3), 50, 8)); // 400
            seedA.DailyLoads.Add(BuildLoad(athlete2, new DateOnly(2026, 11, 5), 60, 5)); // 300

            // Registro soft-deleted en Semana 3: NO debe contarse
            var deletedCheckIn = BuildCheckIn(athlete1, new DateOnly(2026, 11, 10), 1, 1, 5, 5, 5);
            var deletedLoad = BuildLoad(athlete1, new DateOnly(2026, 11, 10), 100, 10);
            seedA.CheckIns.Add(deletedCheckIn);
            seedA.DailyLoads.Add(deletedLoad);

            // Semana 4 (2026-11-16..11-22): Atleta 1
            seedA.CheckIns.Add(BuildCheckIn(athlete1, new DateOnly(2026, 11, 17), 4, 4, 2, 2, 2)); // 3.6
            seedA.DailyLoads.Add(BuildLoad(athlete1, new DateOnly(2026, 11, 17), 40, 6));         // 240

            await seedA.SaveChangesAsync();

            seedA.CheckIns.Remove(deletedCheckIn);
            seedA.DailyLoads.Remove(deletedLoad);
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(new TestCurrentUser(pfB, role: "PreparadorFisico")))
        {
            seedB.CheckIns.Add(BuildCheckIn(foreignAthlete, new DateOnly(2026, 11, 17), 1, 1, 5, 5, 5));
            seedB.DailyLoads.Add(BuildLoad(foreignAthlete, new DateOnly(2026, 11, 17), 90, 9));
            await seedB.SaveChangesAsync();
        }

        var structureJson = $$"""
            [
                { "id": "{{athlete1}}", "fullName": "Atleta Uno" },
                { "id": "{{athlete2}}", "fullName": "Atleta Dos" }
            ]
            """;

        var structureClient = new StructureClient(
            new HttpClient(new FakeHttpMessageHandler(req =>
            {
                if (req.RequestUri!.AbsolutePath == $"/api/v1/categories/{categoryId}/athletes")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(structureJson, Encoding.UTF8, "application/json")
                    };
                }
                if (req.RequestUri!.AbsolutePath == $"/api/v1/athletes/{athlete1}")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }))
            {
                BaseAddress = new Uri("http://estructura.test/")
            },
            NullLogger<StructureClient>.Instance,
            new StubServiceTokenFactory());

        var pfAUser = new TestCurrentUser(pfA, role: "PreparadorFisico");
        await using var readContextA = CreateContext(pfAUser);

        var athleteHandler = new GetAthleteWeeklyTrendsQueryHandler(
            new AthleteAccessGuard(pfAUser, structureClient),
            new CheckInRepository(readContextA),
            new DailyLoadRepository(readContextA),
            fixedClock);

        var categoryHandler = new GetCategoryWeeklyTrendsQueryHandler(
            structureClient,
            new CheckInRepository(readContextA),
            new DailyLoadRepository(readContextA),
            fixedClock);

        var athleteTrends = await athleteHandler.HandleAsync(new GetAthleteWeeklyTrendsQuery(athlete1, Weeks: 4));
        var categoryTrends = await categoryHandler.HandleAsync(new GetCategoryWeeklyTrendsQuery(categoryId, Weeks: 4));

        // Individual: 4 semanas, Semana 3 vacía por soft-delete
        Assert.Equal(4, athleteTrends.Weeks.Count);
        Assert.Equal(4.0, athleteTrends.Weeks[0].AverageWellness);
        Assert.Equal(300, athleteTrends.Weeks[0].TotalLoad);
        Assert.Null(athleteTrends.Weeks[2].AverageWellness);
        Assert.Null(athleteTrends.Weeks[2].TotalLoad);
        Assert.Equal(0, athleteTrends.Weeks[2].CheckInCount);
        Assert.Equal(0, athleteTrends.Weeks[2].LoadCount);
        Assert.Equal(4.0, athleteTrends.Weeks[3].AverageWellness);
        Assert.Equal(240, athleteTrends.Weeks[3].TotalLoad);

        // Grupal: Semana 2 usa G2 -> (5.0 + 1.0) / 2 = 3.0 y carga = 700
        Assert.Equal(2, categoryTrends.AthleteCount);
        Assert.Equal(4, categoryTrends.Weeks.Count);
        Assert.Equal(3.0, categoryTrends.Weeks[1].AverageWellness);
        Assert.Equal(700, categoryTrends.Weeks[1].TotalLoad);
        Assert.Equal(2, categoryTrends.Weeks[1].ActiveReportingAthletes);
        Assert.Null(categoryTrends.Weeks[2].AverageWellness);
        Assert.Null(categoryTrends.Weeks[2].TotalLoad);
    }

    private AppDbContext CreateContext(TestCurrentUser currentUser) =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options,
            currentUser);

    private static CheckIn BuildCheckIn(
        Guid athleteId,
        DateOnly date,
        int sleepQuality,
        int moodRate,
        int fatigueLevel,
        int muscleSoreness,
        int stressLevel) =>
        new()
        {
            AthleteId = athleteId,
            Date = date.ToDateTime(TimeOnly.MinValue),
            TrafficLight = TrafficLight.Green,
            SleepHours = 8.0,
            SleepQuality = sleepQuality,
            MuscleSoreness = muscleSoreness,
            FatigueLevel = fatigueLevel,
            MoodRate = moodRate,
            StressLevel = stressLevel
        };

    private static DailyLoad BuildLoad(Guid athleteId, DateOnly date, int minutes, int rpe) =>
        DailyLoad.Register(
            athleteId,
            date.ToDateTime(TimeOnly.MinValue),
            minutes,
            rpe,
            0,
            null,
            0,
            null,
            0,
            null,
            0,
            null);
}
