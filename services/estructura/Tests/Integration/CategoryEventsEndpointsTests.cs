using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Common;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Estructura.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class CategoryEventsEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;

    public CategoryEventsEndpointsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _pfBId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);
    }

    private async Task<(Guid OrganizationId, Guid CategoryId)> CreateCategoryForPfAAsync(string name = "Primera Division")
    {
        var orgRes = await _clientPfA.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club Atletico " + Guid.NewGuid(), null));
        Assert.Equal(HttpStatusCode.Created, orgRes.StatusCode);
        var org = await orgRes.Content.ReadFromJsonAsync<OrganizationDto>();

        var catRes = await _clientPfA.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, name, null));
        Assert.Equal(HttpStatusCode.Created, catRes.StatusCode);
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryDto>();

        return (org.Id, cat!.Id);
    }

    [Fact]
    public async Task EV_01_Given_UpcomingSaturdayMatch_When_GetEvents_Then_ReturnsEventDetails()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilSaturday == 0) daysUntilSaturday = 7;
        var upcomingSaturday = today.AddDays(daysUntilSaturday);

        var createReq = new CreateCompetitiveEventRequest(
            upcomingSaturday,
            new TimeOnly(15, 30),
            "Rival FC",
            CompetitiveEventVenue.Home,
            CompetitiveEventType.Official,
            "Partido clave por el torneo");

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", createReq);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        var getRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        var ev = Assert.Single(events);
        Assert.Equal(upcomingSaturday, ev.Date);
        Assert.Equal(new TimeOnly(15, 30), ev.StartTime);
        Assert.Equal("Rival FC", ev.Opponent);
        Assert.Equal(CompetitiveEventVenue.Home, ev.Venue);
        Assert.Equal(CompetitiveEventType.Official, ev.Type);
        Assert.Equal("Partido clave por el torneo", ev.Notes);
    }

    [Fact]
    public async Task EV_02_Given_MultipleEvents_When_GetEvents_Then_ReturnsOrderedChronologically()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        var e1 = new CreateCompetitiveEventRequest(today.AddDays(5), new TimeOnly(18, 0), "Rival C", CompetitiveEventVenue.Away, CompetitiveEventType.Official, null);
        var e2 = new CreateCompetitiveEventRequest(today.AddDays(2), new TimeOnly(10, 0), "Rival A", CompetitiveEventVenue.Home, CompetitiveEventType.Friendly, null);
        var e3 = new CreateCompetitiveEventRequest(today.AddDays(2), new TimeOnly(16, 0), "Rival B", CompetitiveEventVenue.Home, CompetitiveEventType.Friendly, null);

        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", e1);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", e2);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", e3);

        var getRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Equal(3, events.Count);
        Assert.Equal("Rival A", events[0].Opponent);
        Assert.Equal("Rival B", events[1].Opponent);
        Assert.Equal("Rival C", events[2].Opponent);
    }

    [Fact]
    public async Task EV_03_And_EV_04_Given_EventTwoDaysBeforeArgentinaToday_When_GetEventsWithoutFrom_Then_PastEventIsExcluded()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        var pastEvent = new CreateCompetitiveEventRequest(today.AddDays(-2), new TimeOnly(15, 0), "Rival Pasado", CompetitiveEventVenue.Away, CompetitiveEventType.Official, null);
        var futureEvent = new CreateCompetitiveEventRequest(today.AddDays(1), new TimeOnly(15, 0), "Rival Futuro", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null);

        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", pastEvent);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", futureEvent);

        var getRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("Rival Futuro", events[0].Opponent);
        Assert.DoesNotContain(events, e => e.Opponent == "Rival Pasado");
    }

    [Fact]
    public async Task EV_05_Given_GetWithoutTo_When_Requested_Then_UpperBoundEqualsFromPlus14Days()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        var withinWindow = new CreateCompetitiveEventRequest(today.AddDays(14), new TimeOnly(10, 0), "Rival 14 Dias", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null);
        var outsideWindow = new CreateCompetitiveEventRequest(today.AddDays(15), new TimeOnly(10, 0), "Rival 15 Dias", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null);

        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", withinWindow);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", outsideWindow);

        var getRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Contains(events, e => e.Opponent == "Rival 14 Dias");
        Assert.DoesNotContain(events, e => e.Opponent == "Rival 15 Dias");
    }

    [Fact]
    public async Task EV_06_Given_ExplicitHistoricalDateRange_When_Requested_Then_MatchingHistoricalEventsReturned()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var historyDate = new DateOnly(2025, 5, 10);

        var historicalEvent = new CreateCompetitiveEventRequest(historyDate, new TimeOnly(16, 0), "Rival Historico", CompetitiveEventVenue.Neutral, CompetitiveEventType.Tournament, null);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events", historicalEvent);

        var url = $"/api/v1/categories/{categoryId}/events?from=2025-05-01&to=2025-05-31";
        var getRes = await _clientPfA.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();

        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("Rival Historico", events[0].Opponent);
    }

    [Fact]
    public async Task EV_07_Given_InvalidDateRangeOrFormat_When_Requested_Then_Returns400BadRequest()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        // to < from
        var toBeforeFrom = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events?from=2026-11-20&to=2026-11-10");
        Assert.Equal(HttpStatusCode.BadRequest, toBeforeFrom.StatusCode);

        // invalid format
        var invalidFrom = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events?from=invalid-date");
        Assert.Equal(HttpStatusCode.BadRequest, invalidFrom.StatusCode);

        var invalidTo = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events?to=20-11-2026");
        Assert.Equal(HttpStatusCode.BadRequest, invalidTo.StatusCode);
    }

    [Fact]
    public async Task EV_08_Given_SoftDeletedEvent_When_GetEvents_Then_ExcludedFromResults()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(3), new TimeOnly(15, 0), "Rival A Eliminar", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        var created = await postRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(created);

        // Soft delete
        var delRes = await _clientPfA.DeleteAsync($"/api/v1/categories/{categoryId}/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Verify excluded
        var getRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var events = await getRes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();
        Assert.NotNull(events);
        Assert.DoesNotContain(events, e => e.Id == created.Id);
    }

    [Fact]
    public async Task EV_09_And_EV_10_Given_AthleteMembership_When_GetEvents_Then_AssignedSucceedsAndUnassignedGets404()
    {
        var (_, categoryAId) = await CreateCategoryForPfAAsync("Categoria A");
        var (_, categoryBId) = await CreateCategoryForPfAAsync("Categoria B");
        var today = ArgentinaTime.Today;

        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryAId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(2), new TimeOnly(16, 0), "Rival Cat A", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));

        // Create athlete under PF-A
        var athleteRes = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Joaquin", "Perez", "joaquin@club.com", "+5491111111111", false));
        Assert.Equal(HttpStatusCode.Created, athleteRes.StatusCode);
        var athlete = await athleteRes.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        // Assign to Category A only
        var assignRes = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{categoryAId}", null);
        Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);

        // Link athlete account in DB
        var accountId = Guid.NewGuid();
        using (var db = _factory.CreateDbContext(_pfAId))
        {
            var dbAthlete = await db.Athletes.FindAsync(athlete.Id);
            dbAthlete!.LinkAccount(accountId);
            await db.SaveChangesAsync();
        }

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: accountId);

        // EV-09: Assigned category A succeeds
        var readCatARes = await athleteClient.GetAsync($"/api/v1/categories/{categoryAId}/events");
        Assert.Equal(HttpStatusCode.OK, readCatARes.StatusCode);
        var events = await readCatARes.Content.ReadFromJsonAsync<List<CompetitiveEventDto>>();
        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("Rival Cat A", events[0].Opponent);

        // EV-10: Unassigned category B returns 404
        var readCatBRes = await athleteClient.GetAsync($"/api/v1/categories/{categoryBId}/events");
        Assert.Equal(HttpStatusCode.NotFound, readCatBRes.StatusCode);
    }

    [Fact]
    public async Task EV_11_To_EV_13_OwnerPf_Can_Create_Update_And_SoftDelete_Event()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        // EV-11: Create
        var createRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(4), new TimeOnly(14, 0), "Rival Inicial", CompetitiveEventVenue.Home, CompetitiveEventType.Friendly, "Nota inicial"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await createRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(created);
        Assert.Equal("Rival Inicial", created.Opponent);

        // EV-12: Update
        var updateRes = await _clientPfA.PutAsJsonAsync($"/api/v1/categories/{categoryId}/events/{created.Id}",
            new UpdateCompetitiveEventRequest(today.AddDays(5), new TimeOnly(17, 30), "Rival Modificado", CompetitiveEventVenue.Away, CompetitiveEventType.Official, "Nota modificada"));
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(updated);
        Assert.Equal("Rival Modificado", updated.Opponent);
        Assert.Equal(CompetitiveEventVenue.Away, updated.Venue);
        Assert.Equal(new TimeOnly(17, 30), updated.StartTime);

        // EV-13: Soft delete
        var deleteRes = await _clientPfA.DeleteAsync($"/api/v1/categories/{categoryId}/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Verify in DB that deleted_at is set
        using var db = _factory.CreateDbContext();
        var dbEvent = await db.CompetitiveEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == created.Id);
        Assert.NotNull(dbEvent);
        Assert.NotNull(dbEvent.DeletedAt);
    }

    [Fact]
    public async Task EV_14_Given_AnotherPf_When_AccessingEvents_Then_Returns404NotFound()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(3), null, "Rival Confidencial", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        var created = await postRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(created);

        // PF-B tries GET
        var getRes = await _clientPfB.GetAsync($"/api/v1/categories/{categoryId}/events");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        // PF-B tries POST
        var postPfB = await _clientPfB.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(3), null, "Rival Hack", CompetitiveEventVenue.Away, CompetitiveEventType.Official, null));
        Assert.Equal(HttpStatusCode.NotFound, postPfB.StatusCode);

        // PF-B tries PUT
        var putPfB = await _clientPfB.PutAsJsonAsync($"/api/v1/categories/{categoryId}/events/{created.Id}",
            new UpdateCompetitiveEventRequest(today.AddDays(3), null, "Rival Hack", CompetitiveEventVenue.Away, CompetitiveEventType.Official, null));
        Assert.Equal(HttpStatusCode.NotFound, putPfB.StatusCode);

        // PF-B tries DELETE
        var delPfB = await _clientPfB.DeleteAsync($"/api/v1/categories/{categoryId}/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delPfB.StatusCode);
    }

    [Fact]
    public async Task EV_15_Given_Athlete_When_AttemptingWriteOperations_Then_Returns403Forbidden()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();
        var today = ArgentinaTime.Today;

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(3), null, "Rival Existente", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        var created = await postRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(created);

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: Guid.NewGuid());

        // Attempt POST
        var postResAthlete = await athleteClient.PostAsJsonAsync($"/api/v1/categories/{categoryId}/events",
            new CreateCompetitiveEventRequest(today.AddDays(4), null, "Rival Atleta", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        Assert.Equal(HttpStatusCode.Forbidden, postResAthlete.StatusCode);

        // Attempt PUT
        var putResAthlete = await athleteClient.PutAsJsonAsync($"/api/v1/categories/{categoryId}/events/{created.Id}",
            new UpdateCompetitiveEventRequest(today.AddDays(4), null, "Rival Atleta", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        Assert.Equal(HttpStatusCode.Forbidden, putResAthlete.StatusCode);

        // Attempt DELETE
        var delResAthlete = await athleteClient.DeleteAsync($"/api/v1/categories/{categoryId}/events/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, delResAthlete.StatusCode);
    }

    [Fact]
    public async Task EV_16_Given_EventOfAnotherCategory_When_ModifyingThroughWrongCategoryRoute_Then_Returns404NotFound()
    {
        var (_, category1) = await CreateCategoryForPfAAsync("Categoria 1");
        var (_, category2) = await CreateCategoryForPfAAsync("Categoria 2");
        var today = ArgentinaTime.Today;

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{category1}/events",
            new CreateCompetitiveEventRequest(today.AddDays(2), null, "Rival Cat 1", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        var event1 = await postRes.Content.ReadFromJsonAsync<CompetitiveEventDto>();
        Assert.NotNull(event1);

        // Try updating event1 using category2 in route
        var wrongPut = await _clientPfA.PutAsJsonAsync($"/api/v1/categories/{category2}/events/{event1.Id}",
            new UpdateCompetitiveEventRequest(today.AddDays(2), null, "Rival Modificado Mal", CompetitiveEventVenue.Home, CompetitiveEventType.Official, null));
        Assert.Equal(HttpStatusCode.NotFound, wrongPut.StatusCode);

        // Try deleting event1 using category2 in route
        var wrongDel = await _clientPfA.DeleteAsync($"/api/v1/categories/{category2}/events/{event1.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrongDel.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
