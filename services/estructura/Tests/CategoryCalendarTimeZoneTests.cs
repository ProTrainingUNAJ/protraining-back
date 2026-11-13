using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Common;
using Domain.Entities;
using Estructura.Tests.Infrastructure;

namespace Estructura.Tests;

[Trait("Category", "Isolation")]
public sealed class CategoryCalendarTimeZoneTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public void TZ_01_CurrentDateCalculatedUsingArgentinaTimeZone()
    {
        // 2026-06-15 15:00 UTC -> 2026-06-15 12:00 in Buenos Aires (UTC-3)
        var utcInstant = new DateTime(2026, 6, 15, 15, 0, 0, DateTimeKind.Utc);
        var argTime = ArgentinaTime.FromUtc(utcInstant);

        Assert.Equal(2026, argTime.Year);
        Assert.Equal(6, argTime.Month);
        Assert.Equal(15, argTime.Day);
        Assert.Equal(12, argTime.Hour);
    }

    [Fact]
    public async Task TZ_02_NearMidnightUtc_DefaultFromDateCorrespondsToArgentinaCalendarDate()
    {
        // 2026-11-02 01:15:00 UTC is 2026-11-01 22:15:00 in Argentina (UTC-3)
        // UTC calendar date is Nov 2nd, but Argentina calendar date is Nov 1st!
        var nearMidnightUtc = new DateTimeOffset(2026, 11, 2, 1, 15, 0, TimeSpan.Zero);
        var fakeClock = new FixedTimeProvider(nearMidnightUtc);

        await using var factory = new EstructuraTestApplicationFactory(customTimeProvider: fakeClock);
        var pfId = Guid.NewGuid();
        var client = factory.CreateAuthenticatedClient(pfId);

        // Create Org and Category
        var orgRes = await client.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club TZ", null));
        var org = await orgRes.Content.ReadFromJsonAsync<OrganizationDto>();
        var catRes = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, "Categoria TZ", null));
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryDto>();

        // Create event on 2026-11-01 (Argentina today, but UTC yesterday)
        var nov1Event = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 1),
            new TimeOnly(20, 0),
            "Rival 1 de Noviembre",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            null);
        await client.PostAsJsonAsync($"/api/v1/categories/{cat!.Id}/events", nov1Event);

        // GET without 'from' should compute today as 2026-11-01 in Argentina, so nov1Event IS included!
        var getRes = await client.GetAsync($"/api/v1/categories/{cat.Id}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("Rival 1 de Noviembre", events[0].Opponent);
        Assert.Equal(new DateOnly(2026, 11, 1), events[0].Date);
    }

    [Fact]
    public async Task TZ_03_ExplicitFromAndTo_TreatedAsCalendarDatesWithoutUnintendedUtcShifts()
    {
        var fixedClock = new FixedTimeProvider(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new EstructuraTestApplicationFactory(customTimeProvider: fixedClock);
        var pfId = Guid.NewGuid();
        var client = factory.CreateAuthenticatedClient(pfId);

        var orgRes = await client.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club TZ 2", null));
        var org = await orgRes.Content.ReadFromJsonAsync<OrganizationDto>();
        var catRes = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, "Categoria TZ 2", null));
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryDto>();

        var eventMay1 = new CreateCompetitiveEventRequest(new DateOnly(2026, 5, 1), null, "Rival 1 de Mayo", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null);
        var eventMay2 = new CreateCompetitiveEventRequest(new DateOnly(2026, 5, 2), null, "Rival 2 de Mayo", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null);

        await client.PostAsJsonAsync($"/api/v1/categories/{cat!.Id}/events", eventMay1);
        await client.PostAsJsonAsync($"/api/v1/categories/{cat.Id}/events", eventMay2);

        // Query exactly for 2026-05-01
        var singleDayRes = await client.GetAsync($"/api/v1/categories/{cat.Id}/events?from=2026-05-01&to=2026-05-01");
        Assert.Equal(HttpStatusCode.OK, singleDayRes.StatusCode);
        var events = await singleDayRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal(new DateOnly(2026, 5, 1), events[0].Date);
        Assert.Equal("Rival 1 de Mayo", events[0].Opponent);
    }
}
