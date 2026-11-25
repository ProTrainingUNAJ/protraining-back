using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Trends;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class WeeklyTrendsEndpointIntegrationTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // Martes 2026-11-17 15:00 Argentina (18:00 UTC)
    private static FixedTimeProvider CreateFixedClock() =>
        new(new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task KAN489_01_PfQueriesAthleteTrends_ReturnsFourOrderedWeeks()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(
            pfId: pfId,
            customTimeProvider: CreateFixedClock());

        factory.Structure.RespondAllAthletesAreOwn();

        await SeedAthleteDataAsync(factory, pfId, athleteId,
            checkInDates:
            [
                new DateOnly(2026, 10, 27),
                new DateOnly(2026, 11, 4),
                new DateOnly(2026, 11, 12),
                new DateOnly(2026, 11, 17)
            ],
            loadEntries:
            [
                (new DateOnly(2026, 10, 28), 60, 6),
                (new DateOnly(2026, 11, 5), 80, 7),
                (new DateOnly(2026, 11, 11), 50, 8),
                (new DateOnly(2026, 11, 16), 45, 6)
            ]);

        var client = factory.CreateAuthenticatedClient(pfId, "PreparadorFisico");
        var response = await client.GetAsync($"/api/v1/athletes/{athleteId}/trends");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AthleteWeeklyTrendsResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(athleteId, dto.AthleteId);
        Assert.Equal(4, dto.Weeks.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), dto.Weeks[0].WeekStartDate);
        Assert.Equal(new DateOnly(2026, 11, 16), dto.Weeks[3].WeekStartDate);
        Assert.Equal(360, dto.Weeks[0].TotalLoad);
        Assert.Equal(560, dto.Weeks[1].TotalLoad);
        Assert.Equal(400, dto.Weeks[2].TotalLoad);
        Assert.Equal(270, dto.Weeks[3].TotalLoad);
    }

    [Fact]
    public async Task KAN489_06_PfCannotAccessForeignAthleteOrForeignCategoryTrends_Returns404()
    {
        var pfBId = Guid.NewGuid();
        var foreignAthleteId = Guid.NewGuid();
        var foreignCategoryId = Guid.NewGuid();

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(
            pfId: pfBId,
            customTimeProvider: CreateFixedClock());

        // Estructura responde 404 porque el recurso pertenece a otro PF.
        factory.Structure.RespondWith(HttpStatusCode.NotFound, "{}");

        var pfBClient = factory.CreateAuthenticatedClient(pfBId, "PreparadorFisico");

        var athleteRes = await pfBClient.GetAsync($"/api/v1/athletes/{foreignAthleteId}/trends");
        var categoryRes = await pfBClient.GetAsync($"/api/v1/categories/{foreignCategoryId}/trends");

        Assert.Equal(HttpStatusCode.NotFound, athleteRes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, categoryRes.StatusCode);
    }

    [Fact]
    public async Task KAN489_07_AthleteCanOnlyAccessOwnTrends_AndReceives404ForOthersOr403ForCategory()
    {
        var pfId = Guid.NewGuid();
        var myAthleteId = Guid.NewGuid();
        var otherAthleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(
            pfId: pfId,
            customTimeProvider: CreateFixedClock());

        factory.Structure.Behavior = (req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v1/me")
            {
                var json = $$"""{"athleteId":"{{myAthleteId}}","pfId":"{{pfId}}","categories":[]}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        await SeedAthleteDataAsync(factory, pfId, myAthleteId,
            checkInDates: [new DateOnly(2026, 11, 17)],
            loadEntries: [(new DateOnly(2026, 11, 17), 40, 5)]);

        var athleteClient = factory.CreateAuthenticatedClient(pfId, "Atleta");

        var ownRes = await athleteClient.GetAsync($"/api/v1/athletes/{myAthleteId}/trends");
        Assert.Equal(HttpStatusCode.OK, ownRes.StatusCode);
        var ownDto = await ownRes.Content.ReadFromJsonAsync<AthleteWeeklyTrendsResponseDto>();
        Assert.NotNull(ownDto);
        Assert.Equal(4, ownDto.Weeks.Count);
        Assert.Equal(200, ownDto.Weeks[3].TotalLoad);

        var otherRes = await athleteClient.GetAsync($"/api/v1/athletes/{otherAthleteId}/trends");
        Assert.Equal(HttpStatusCode.NotFound, otherRes.StatusCode);

        var categoryRes = await athleteClient.GetAsync($"/api/v1/categories/{categoryId}/trends");
        Assert.Equal(HttpStatusCode.Forbidden, categoryRes.StatusCode);
    }

    [Fact]
    public async Task KAN489_UpperBoundary_ExcludesRecordsAfterTodayAndAfterToParam_IncludesToDate_Individual()
    {
        var pfId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        // Reloj fijo: martes 2026-11-17 en Argentina (semana actual: 2026-11-16..2026-11-22)
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(
            pfId: pfId,
            customTimeProvider: CreateFixedClock());

        factory.Structure.RespondAllAthletesAreOwn();

        // Incluir registro en 'today' (2026-11-17), un registro futuro dentro de la misma semana (2026-11-19),
        // un registro en 'to' (2026-11-11) y otro posterior a 'to' dentro de esa misma semana (2026-11-13).
        await SeedAthleteDataAsync(factory, pfId, athleteId,
            checkInDates:
            [
                new DateOnly(2026, 11, 11),
                new DateOnly(2026, 11, 13),
                new DateOnly(2026, 11, 17),
                new DateOnly(2026, 11, 19)
            ],
            loadEntries:
            [
                (new DateOnly(2026, 11, 11), 40, 5), // 200
                (new DateOnly(2026, 11, 13), 80, 9), // 720 (no debe entrar cuando to=2026-11-11)
                (new DateOnly(2026, 11, 17), 50, 6), // 300 (debe entrar cuando today=2026-11-17)
                (new DateOnly(2026, 11, 19), 90, 9)  // 810 (futuro respecto a today=2026-11-17, no debe entrar)
            ]);

        var client = factory.CreateAuthenticatedClient(pfId, "PreparadorFisico");

        // 1. Sin parámetro 'to' (usa today = 2026-11-17): excluye 2026-11-19 pero incluye 2026-11-17.
        var defaultRes = await client.GetAsync($"/api/v1/athletes/{athleteId}/trends?weeks=4");
        Assert.Equal(HttpStatusCode.OK, defaultRes.StatusCode);
        var defaultDto = await defaultRes.Content.ReadFromJsonAsync<AthleteWeeklyTrendsResponseDto>();
        Assert.NotNull(defaultDto);
        Assert.Equal(4, defaultDto.Weeks.Count);
        Assert.Null(defaultDto.Weeks[0].TotalLoad); // Semana vacía sigue en null
        Assert.Equal(0, defaultDto.Weeks[0].CheckInCount);
        Assert.Equal(new DateOnly(2026, 11, 16), defaultDto.Weeks[3].WeekStartDate);
        Assert.Equal(new DateOnly(2026, 11, 22), defaultDto.Weeks[3].WeekEndDate);
        Assert.Equal(300, defaultDto.Weeks[3].TotalLoad);
        Assert.Equal(1, defaultDto.Weeks[3].CheckInCount);
        Assert.Equal(1, defaultDto.Weeks[3].LoadCount);

        // 2. Con to=2026-11-11: incluye 2026-11-11 y excluye 2026-11-13 (misma semana 2026-11-09..15).
        var customToRes = await client.GetAsync($"/api/v1/athletes/{athleteId}/trends?weeks=4&to=2026-11-11");
        Assert.Equal(HttpStatusCode.OK, customToRes.StatusCode);
        var customToDto = await customToRes.Content.ReadFromJsonAsync<AthleteWeeklyTrendsResponseDto>();
        Assert.NotNull(customToDto);
        Assert.Equal(4, customToDto.Weeks.Count);
        Assert.Equal(new DateOnly(2026, 11, 9), customToDto.Weeks[3].WeekStartDate);
        Assert.Equal(200, customToDto.Weeks[3].TotalLoad);
        Assert.Equal(1, customToDto.Weeks[3].CheckInCount);
        Assert.Equal(1, customToDto.Weeks[3].LoadCount);
    }

    [Fact]
    public async Task KAN489_UpperBoundary_ExcludesRecordsAfterTodayAndAfterToParam_IncludesToDate_Category()
    {
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(
            pfId: pfId,
            customTimeProvider: CreateFixedClock());

        factory.Structure.RespondWithAthletes($$"""[{"id":"{{athleteId}}","fullName":"Atleta Prueba"}]""");

        await SeedAthleteDataAsync(factory, pfId, athleteId,
            checkInDates:
            [
                new DateOnly(2026, 11, 11),
                new DateOnly(2026, 11, 13),
                new DateOnly(2026, 11, 17),
                new DateOnly(2026, 11, 19)
            ],
            loadEntries:
            [
                (new DateOnly(2026, 11, 11), 30, 5), // 150
                (new DateOnly(2026, 11, 13), 60, 8), // 480
                (new DateOnly(2026, 11, 17), 40, 5), // 200
                (new DateOnly(2026, 11, 19), 70, 9)  // 630
            ]);

        var client = factory.CreateAuthenticatedClient(pfId, "PreparadorFisico");

        var defaultRes = await client.GetAsync($"/api/v1/categories/{categoryId}/trends?weeks=4");
        Assert.Equal(HttpStatusCode.OK, defaultRes.StatusCode);
        var defaultDto = await defaultRes.Content.ReadFromJsonAsync<CategoryWeeklyTrendsResponseDto>();
        Assert.NotNull(defaultDto);
        Assert.Equal(4, defaultDto.Weeks.Count);
        Assert.Equal(200, defaultDto.Weeks[3].TotalLoad);
        Assert.Equal(1, defaultDto.Weeks[3].CheckInCount);
        Assert.Equal(1, defaultDto.Weeks[3].LoadCount);

        var customToRes = await client.GetAsync($"/api/v1/categories/{categoryId}/trends?weeks=4&to=2026-11-11");
        Assert.Equal(HttpStatusCode.OK, customToRes.StatusCode);
        var customToDto = await customToRes.Content.ReadFromJsonAsync<CategoryWeeklyTrendsResponseDto>();
        Assert.NotNull(customToDto);
        Assert.Equal(4, customToDto.Weeks.Count);
        Assert.Equal(150, customToDto.Weeks[3].TotalLoad);
        Assert.Equal(1, customToDto.Weeks[3].CheckInCount);
        Assert.Equal(1, customToDto.Weeks[3].LoadCount);
    }

    private static Task SeedAthleteDataAsync(
        SeguimientoTestApplicationFactory factory,
        Guid pfId,
        Guid athleteId,
        IReadOnlyList<DateOnly> checkInDates,
        IReadOnlyList<(DateOnly Date, int Minutes, int Rpe)> loadEntries) =>
        factory.ExecuteInDbContextAsync(async db =>
        {
            foreach (var date in checkInDates)
            {
                db.CheckIns.Add(new CheckIn
                {
                    AthleteId = athleteId,
                    Date = date.ToDateTime(TimeOnly.MinValue),
                    TrafficLight = TrafficLight.Green,
                    SleepHours = 8.0,
                    SleepQuality = 4,
                    MuscleSoreness = 2,
                    FatigueLevel = 2,
                    MoodRate = 4,
                    StressLevel = 2
                });
            }

            foreach (var (date, minutes, rpe) in loadEntries)
            {
                var load = DailyLoad.Register(
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
                db.DailyLoads.Add(load);
            }

            await db.SaveChangesAsync();
        });
}
