using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Common;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Estructura.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class CategoryEventDateValidationTests : IAsyncDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private readonly DateTimeOffset _referenceUtc;
    private readonly FixedTimeProvider _fixedClock;
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfId;
    private readonly HttpClient _client;

    public CategoryEventDateValidationTests()
    {
        // 2026-11-04 15:00:00 UTC corresponds to 2026-11-04 12:00:00 in Argentina (UTC-3)
        _referenceUtc = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        _fixedClock = new FixedTimeProvider(_referenceUtc);
        _factory = new EstructuraTestApplicationFactory(customTimeProvider: _fixedClock);
        _pfId = Guid.NewGuid();
        _client = _factory.CreateAuthenticatedClient(_pfId);
    }

    private async Task<Guid> CreateCategoryAsync(string name = "Categoria KAN-405")
    {
        var orgRes = await _client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Club Atletico KAN-405 " + Guid.NewGuid(), null));
        Assert.Equal(HttpStatusCode.Created, orgRes.StatusCode);
        var org = await orgRes.Content.ReadFromJsonAsync<OrganizationDto>();

        var catRes = await _client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest(org!.Id, name, null));
        Assert.Equal(HttpStatusCode.Created, catRes.StatusCode);
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryDto>();

        return cat!.Id;
    }

    /// <summary>
    /// KAN-405 Mandatory Acceptance Criterion:
    /// GIVEN a competitive event that occurred two days ago,
    /// WHEN an authorized user requests upcoming events for the next 14 days,
    /// THEN the past event is excluded from the returned list.
    ///
    /// Reference Argentina Date: 2026-11-04
    /// Test Data:
    /// - Past match: 2026-11-02 -> Excluded
    /// - Today's match: 2026-11-04 -> Included
    /// - Upcoming match: 2026-11-07 -> Included
    /// - Match at day +14: 2026-11-18 -> Included
    /// - Match at day +15: 2026-11-19 -> Excluded
    /// </summary>
    [Fact]
    public async Task KAN405_PastEventExcluded()
    {
        // Arrange
        var categoryId = await CreateCategoryAsync("Categoria KAN405 Principal");

        var pastMatch = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 2),
            new TimeOnly(15, 0),
            "Past match",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Official,
            "Partido de hace dos días");

        var todaysMatch = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 4),
            new TimeOnly(18, 0),
            "Today's match",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            "Partido de hoy");

        var upcomingMatch = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 7),
            new TimeOnly(16, 30),
            "Upcoming match",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Friendly,
            "Partido próximo");

        var matchDay14 = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 18),
            new TimeOnly(20, 0),
            "Match at day +14",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Official,
            "Partido en el límite de la ventana");

        var matchDay15 = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 19),
            new TimeOnly(10, 0),
            "Match at day +15",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Tournament,
            "Partido fuera de la ventana");

        var resPast = await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", pastMatch);
        var resToday = await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", todaysMatch);
        var resUpcoming = await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", upcomingMatch);
        var resDay14 = await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", matchDay14);
        var resDay15 = await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", matchDay15);

        Assert.Equal(HttpStatusCode.Created, resPast.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resToday.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resUpcoming.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resDay14.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resDay15.StatusCode);

        // Act: GET without 'from' and 'to'
        var getRes = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");

        // Assert 1: The HTTP response is successful
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();
        Assert.NotNull(events);

        // Assert 2: The event from two days ago is absent
        Assert.DoesNotContain(events, e => e.Opponent == "Past match");
        Assert.DoesNotContain(events, e => e.Date == new DateOnly(2026, 11, 2));

        // Assert 3: Today's event is present
        Assert.Contains(events, e => e.Opponent == "Today's match" && e.Date == new DateOnly(2026, 11, 4));

        // Assert 4: The upcoming event is present
        Assert.Contains(events, e => e.Opponent == "Upcoming match" && e.Date == new DateOnly(2026, 11, 7));

        // Assert 5: The event on day +14 is present
        Assert.Contains(events, e => e.Opponent == "Match at day +14" && e.Date == new DateOnly(2026, 11, 18));

        // Assert 6: The event on day +15 is absent
        Assert.DoesNotContain(events, e => e.Opponent == "Match at day +15");
        Assert.DoesNotContain(events, e => e.Date == new DateOnly(2026, 11, 19));

        // Assert 7: Results are sorted chronologically
        Assert.Equal(3, events.Count);
        Assert.Equal("Today's match", events[0].Opponent);
        Assert.Equal(new DateOnly(2026, 11, 4), events[0].Date);

        Assert.Equal("Upcoming match", events[1].Opponent);
        Assert.Equal(new DateOnly(2026, 11, 7), events[1].Date);

        Assert.Equal("Match at day +14", events[2].Opponent);
        Assert.Equal(new DateOnly(2026, 11, 18), events[2].Date);
    }

    [Fact]
    public async Task EV_DATE_01_PastEventExclusion_ExcludedFromDefaultUpcomingQuery()
    {
        var categoryId = await CreateCategoryAsync();
        var pastEvent = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 2),
            new TimeOnly(15, 0),
            "Rival Pasado -2d",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Official,
            null);
        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", pastEvent);

        var getRes = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.DoesNotContain(events, e => e.Opponent == "Rival Pasado -2d");
    }

    [Fact]
    public async Task EV_DATE_02_CurrentDateInclusion_AppearsInResults()
    {
        var categoryId = await CreateCategoryAsync();
        var todayEvent = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 4),
            new TimeOnly(17, 0),
            "Rival Hoy 4 Nov",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            null);
        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", todayEvent);

        var getRes = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Contains(events, e => e.Opponent == "Rival Hoy 4 Nov" && e.Date == new DateOnly(2026, 11, 4));
    }

    [Fact]
    public async Task EV_DATE_03_DefaultRange_CoversTodayThroughDay14Inclusive()
    {
        var categoryId = await CreateCategoryAsync();
        var eventDay14 = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 18),
            new TimeOnly(10, 0),
            "Rival Exacto Day 14",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            null);
        var eventDay15 = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 19),
            new TimeOnly(10, 0),
            "Rival Fuera Day 15",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            null);

        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", eventDay14);
        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", eventDay15);

        var getRes = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Contains(events, e => e.Opponent == "Rival Exacto Day 14");
        Assert.DoesNotContain(events, e => e.Opponent == "Rival Fuera Day 15");
    }

    [Fact]
    public async Task EV_DATE_04_ExplicitHistoricalRange_AllowsQueryingPastEvents()
    {
        var categoryId = await CreateCategoryAsync();
        var pastDate = new DateOnly(2026, 8, 15);
        var historicalEvent = new CreateCompetitiveEventRequest(
            pastDate,
            new TimeOnly(15, 30),
            "Rival Historico Agosto",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Tournament,
            "Partido historico del torneo pasado");

        var createRes = await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", historicalEvent);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        // Query with explicit historical range
        var getRes = await _client.GetAsync($"/api/v1/categories/{categoryId}/events?from=2026-08-01&to=2026-08-31");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        var ev = Assert.Single(events);
        Assert.Equal("Rival Historico Agosto", ev.Opponent);
        Assert.Equal(pastDate, ev.Date);
    }

    [Fact]
    public async Task EV_DATE_05_InvalidQueryRange_ToEarlierThanFrom_Returns400WithSpanishMessage()
    {
        var categoryId = await CreateCategoryAsync();

        var res = await _client.GetAsync($"/api/v1/categories/{categoryId}/events?from=2026-11-20&to=2026-11-10");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("La fecha 'to' no puede ser anterior a la fecha 'from'.", problem.Title);
    }

    [Fact]
    public async Task EV_DATE_06_InvalidQueryDateFormat_Returns400WithSpanishMessage()
    {
        var categoryId = await CreateCategoryAsync();

        // Invalid format in 'from'
        var resFrom = await _client.GetAsync($"/api/v1/categories/{categoryId}/events?from=2026/11/04");
        Assert.Equal(HttpStatusCode.BadRequest, resFrom.StatusCode);
        var problemFrom = await resFrom.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemFrom);
        Assert.Equal("El formato de fecha 'from' es inválido. Debe ser YYYY-MM-DD.", problemFrom.Title);

        // Invalid format in 'to'
        var resTo = await _client.GetAsync($"/api/v1/categories/{categoryId}/events?to=04-11-2026");
        Assert.Equal(HttpStatusCode.BadRequest, resTo.StatusCode);
        var problemTo = await resTo.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemTo);
        Assert.Equal("El formato de fecha 'to' es inválido. Debe ser YYYY-MM-DD.", problemTo.Title);
    }

    [Fact]
    public async Task EV_DATE_07_ArgentinaTimeZone_CalculatedUsingArgentinaRegardlessOfUtcDifference()
    {
        // 2026-11-05 01:30:00 UTC corresponds to 2026-11-04 22:30:00 in Argentina (UTC-3)
        // In UTC it is already November 5th, but in Argentina it is still November 4th!
        var nearMidnightUtc = new DateTimeOffset(2026, 11, 5, 1, 30, 0, TimeSpan.Zero);
        var fakeClock = new FixedTimeProvider(nearMidnightUtc);

        await using var factory = new EstructuraTestApplicationFactory(customTimeProvider: fakeClock);
        var client = factory.CreateAuthenticatedClient(_pfId);

        var orgRes = await client.PostAsJsonAsync(
            "/api/v1/organizations",
            new CreateOrganizationRequest("Club TZ " + Guid.NewGuid(), null));
        var org = await orgRes.Content.ReadFromJsonAsync<OrganizationDto>();
        var catRes = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest(org!.Id, "Categoria TZ", null));
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryDto>();

        // Event on 2026-11-04 (Argentina today, UTC yesterday)
        var nov4Event = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 4),
            new TimeOnly(20, 0),
            "Rival 4 Noviembre",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            null);

        // Event on 2026-11-03 (Argentina past)
        var nov3Event = new CreateCompetitiveEventRequest(
            new DateOnly(2026, 11, 3),
            new TimeOnly(20, 0),
            "Rival 3 Noviembre",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Official,
            null);

        await client.PostAsJsonAsync($"/api/v1/categories/{cat!.Id}/events", nov4Event);
        await client.PostAsJsonAsync($"/api/v1/categories/{cat.Id}/events", nov3Event);

        // Default query without 'from': must resolve 'from' as 2026-11-04
        var getRes = await client.GetAsync($"/api/v1/categories/{cat.Id}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Contains(events, e => e.Opponent == "Rival 4 Noviembre" && e.Date == new DateOnly(2026, 11, 4));
        Assert.DoesNotContain(events, e => e.Opponent == "Rival 3 Noviembre");
    }

    [Fact]
    public async Task EV_DATE_08_DateOrdering_DeterministicForSameDateEvents()
    {
        var categoryId = await CreateCategoryAsync();
        var date = new DateOnly(2026, 11, 10);

        var eventLate = new CreateCompetitiveEventRequest(date, new TimeOnly(18, 0), "Rival Tarde", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null);
        var eventEarly = new CreateCompetitiveEventRequest(date, new TimeOnly(10, 0), "Rival Temprano", CompetitiveEventVenue.Away, CompetitiveEventType.Official, null);
        var eventNoTime = new CreateCompetitiveEventRequest(date, null, "Rival Sin Hora", CompetitiveEventVenue.Neutral, CompetitiveEventType.Friendly, null);

        // Insert in non-sorted order
        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", eventLate);
        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", eventNoTime);
        await _client.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", eventEarly);

        var getRes = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Equal(3, events.Count);
        // Repository orders by Date, StartTime, Id. Null StartTime sorts first in SQL/EF.
        Assert.Equal("Rival Sin Hora", events[0].Opponent);
        Assert.Null(events[0].StartTime);

        Assert.Equal("Rival Temprano", events[1].Opponent);
        Assert.Equal(new TimeOnly(10, 0), events[1].StartTime);

        Assert.Equal("Rival Tarde", events[2].Opponent);
        Assert.Equal(new TimeOnly(18, 0), events[2].StartTime);
    }

    [Fact]
    public async Task EV_DATE_09_SoftDeletedEvents_ExcludedFromUpcomingResults()
    {
        var categoryId = await CreateCategoryAsync();
        var date = new DateOnly(2026, 11, 8);

        var postRes = await _client.PostAsJsonAsync(
            $"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(date, new TimeOnly(16, 0), "Rival A Eliminar", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        var created = await postRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(created);

        // Verify it was created and is visible
        var listBefore = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");
        var eventsBefore = await listBefore.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();
        Assert.Contains(eventsBefore!, e => e.Id == created.Id);

        // Soft delete the event
        var delRes = await _client.DeleteAsync($"/api/v1/categories/{categoryId}/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Act: Request upcoming events
        var listAfter = await _client.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, listAfter.StatusCode);
        var eventsAfter = await listAfter.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(eventsAfter);
        Assert.DoesNotContain(eventsAfter, e => e.Id == created.Id);
    }

    [Fact]
    public async Task EV_DATE_10_EventDateRescheduling_UpdatesDatesProperly()
    {
        var categoryId = await CreateCategoryAsync();
        var originalDate = new DateOnly(2026, 11, 6);
        var newDate = new DateOnly(2026, 11, 12);

        var postRes = await _client.PostAsJsonAsync(
            $"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(originalDate, new TimeOnly(15, 0), "Rival Reprogramable", CompetitiveEventVenue.Home, CompetitiveEventType.Official, "Original"));
        var created = await postRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(created);

        var updateReq = new UpdateCompetitiveEventRequest(
            newDate,
            new TimeOnly(19, 0),
            "Rival Reprogramado",
            CompetitiveEventVenue.Away,
            CompetitiveEventType.Official,
            "Fecha actualizada");

        var putRes = await _client.PutAsJsonAsync($"/api/v1/categories/{categoryId}/events/{created.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var updated = await putRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();

        Assert.NotNull(updated);
        Assert.Equal(newDate, updated.Date);
        Assert.Equal(new TimeOnly(19, 0), updated.StartTime);
        Assert.Equal("Rival Reprogramado", updated.Opponent);
        Assert.Equal(CompetitiveEventVenue.Away, updated.Venue);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
