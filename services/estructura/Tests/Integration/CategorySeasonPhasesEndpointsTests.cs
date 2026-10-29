using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Estructura.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class CategorySeasonPhasesEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;

    public CategorySeasonPhasesEndpointsTests()
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
    public async Task SP_01_Given_ValidData_When_OwnerPfCreatesPhase_Then_Returns201Created()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var createReq = new CreateSeasonPhaseRequest(
            "Pretemporada 2026",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 2, 28));

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", createReq);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);
        var phase = await postRes.Content.ReadFromJsonAsync<SeasonPhaseDto>();

        Assert.NotNull(phase);
        Assert.Equal("Pretemporada 2026", phase.Name);
        Assert.Equal(SeasonPhaseType.Preseason, phase.Type);
        Assert.Equal(new DateOnly(2026, 1, 10), phase.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 28), phase.EndDate);
    }

    [Fact]
    public async Task SP_02_Given_EndDateBeforeStartDate_When_CreatingPhase_Then_Returns400WithSpanishMessage()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var invalidReq = new CreateSeasonPhaseRequest(
            "Fase Invalida",
            SeasonPhaseType.Preseason,
            new DateOnly(2026, 3, 10),
            new DateOnly(2026, 3, 5));

        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", invalidReq);
        Assert.Equal(HttpStatusCode.BadRequest, postRes.StatusCode);
        var problem = await postRes.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("La fecha de finalización no puede ser anterior a la fecha de inicio.", problem.Title);
    }

    [Fact]
    public async Task SP_03_Given_OverlappingPhaseDates_When_CreatingPhase_Then_Returns400WithSpanishMessage()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p1 = new CreateSeasonPhaseRequest("Fase 1", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var post1 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p1);
        Assert.Equal(HttpStatusCode.Created, post1.StatusCode);

        // Overlapping phase: 2026-01-20 to 2026-02-15
        var p2 = new CreateSeasonPhaseRequest("Fase Superpuesta", SeasonPhaseType.Competitive, new DateOnly(2026, 1, 20), new DateOnly(2026, 2, 15));
        var post2 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p2);
        Assert.Equal(HttpStatusCode.BadRequest, post2.StatusCode);
        var problem = await post2.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Equal("Las fechas de la fase se superponen con otra fase de la misma categoría.", problem.Title);
    }

    [Fact]
    public async Task SP_04_Given_NonOverlappingPhases_When_Created_Then_BothCoexistSuccessfully()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p1 = new CreateSeasonPhaseRequest("Pretemporada", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));
        var p2 = new CreateSeasonPhaseRequest("Competitiva Apertura", SeasonPhaseType.Competitive, new DateOnly(2026, 3, 1), new DateOnly(2026, 6, 30));

        var res1 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p1);
        var res2 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p2);

        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);

        var listRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/season-phases");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var phases = await listRes.Content.ReadFromJsonAsync<List<SeasonPhaseDto>>();
        Assert.NotNull(phases);
        Assert.Equal(2, phases.Count);
    }

    [Fact]
    public async Task SP_05_And_SP_06_Given_PhaseUpdate_When_OverlappingAnother_Then_400_When_SelfOverlap_Then_Succeeds()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p1 = new CreateSeasonPhaseRequest("Fase A", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var p2 = new CreateSeasonPhaseRequest("Fase B", SeasonPhaseType.Competitive, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));

        var res1 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p1);
        var phase1 = await res1.Content.ReadFromJsonAsync<SeasonPhaseDto>();

        var res2 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p2);
        var phase2 = await res2.Content.ReadFromJsonAsync<SeasonPhaseDto>();

        // SP-06: Self-overlap when updating Phase 1 (e.g. extending by 5 days within non-conflicting bounds) -> succeeds
        var selfUpdate = new UpdateSeasonPhaseRequest("Fase A Modificada", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 25));
        var selfUpdateRes = await _clientPfA.PutAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases/{phase1!.Id}", selfUpdate);
        Assert.Equal(HttpStatusCode.OK, selfUpdateRes.StatusCode);

        // SP-05: Phase 1 update overlaps Phase 2 -> 400 Bad Request
        var conflictUpdate = new UpdateSeasonPhaseRequest("Fase A Conflicto", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 10));
        var conflictRes = await _clientPfA.PutAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases/{phase1.Id}", conflictUpdate);
        Assert.Equal(HttpStatusCode.BadRequest, conflictRes.StatusCode);
        var problem = await conflictRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Las fechas de la fase se superponen con otra fase de la misma categoría.", problem.Title);
    }

    [Fact]
    public async Task SP_07_Given_SoftDeletedPhase_When_CheckingOverlap_Then_ExcludedFromOverlapChecks()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p1 = new CreateSeasonPhaseRequest("Fase Vieja", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var res1 = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p1);
        var phase1 = await res1.Content.ReadFromJsonAsync<SeasonPhaseDto>();

        // Soft delete phase 1
        var delRes = await _clientPfA.DeleteAsync($"/api/v1/categories/{categoryId}/season-phases/{phase1!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Creating a new phase with the EXACT same dates as deleted phase -> must succeed
        var pNew = new CreateSeasonPhaseRequest("Fase Nueva Reemplazo", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var resNew = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", pNew);
        Assert.Equal(HttpStatusCode.Created, resNew.StatusCode);
    }

    [Fact]
    public async Task SP_08_Given_AnotherPf_When_AccessingPhases_Then_Returns404NotFound()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p = new CreateSeasonPhaseRequest("Fase Confidencial", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p);
        var created = await postRes.Content.ReadFromJsonAsync<SeasonPhaseDto>();

        // PF-B tries GET
        var getRes = await _clientPfB.GetAsync($"/api/v1/categories/{categoryId}/season-phases");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        // PF-B tries POST
        var postResB = await _clientPfB.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p);
        Assert.Equal(HttpStatusCode.NotFound, postResB.StatusCode);

        // PF-B tries PUT
        var putResB = await _clientPfB.PutAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases/{created!.Id}",
            new UpdateSeasonPhaseRequest("Hack", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        Assert.Equal(HttpStatusCode.NotFound, putResB.StatusCode);

        // PF-B tries DELETE
        var delResB = await _clientPfB.DeleteAsync($"/api/v1/categories/{categoryId}/season-phases/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delResB.StatusCode);
    }

    [Fact]
    public async Task SP_09_Given_AuthorizedAthlete_When_AccessingPhases_Then_CanReadButCannotModify()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p = new CreateSeasonPhaseRequest("Fase Oficial", SeasonPhaseType.Competitive, new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 31));
        var postRes = await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p);
        var created = await postRes.Content.ReadFromJsonAsync<SeasonPhaseDto>();

        // Create athlete and associate
        var athleteRes = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Marcos", "Rojo", "marcos@club.com", "+5491122223333", false));
        var athlete = await athleteRes.Content.ReadFromJsonAsync<AthleteDto>();
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete!.Id}/categories/{categoryId}", null);

        var accountId = Guid.NewGuid();
        using (var db = _factory.CreateDbContext(_pfAId))
        {
            var dbAthlete = await db.Athletes.FindAsync(athlete.Id);
            dbAthlete!.LinkAccount(accountId);
            await db.SaveChangesAsync();
        }

        var athleteClient = _factory.CreateAuthenticatedClient(_pfAId, role: "Atleta", userId: accountId);

        // Read succeeds
        var readRes = await athleteClient.GetAsync($"/api/v1/categories/{categoryId}/season-phases");
        Assert.Equal(HttpStatusCode.OK, readRes.StatusCode);
        var phases = await readRes.Content.ReadFromJsonAsync<List<SeasonPhaseDto>>();
        Assert.NotNull(phases);
        Assert.Single(phases);

        // Write fails 403 Forbidden
        var createAttempt = await athleteClient.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p);
        Assert.Equal(HttpStatusCode.Forbidden, createAttempt.StatusCode);

        var updateAttempt = await athleteClient.PutAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases/{created!.Id}",
            new UpdateSeasonPhaseRequest("Hack", SeasonPhaseType.Competitive, new DateOnly(2026, 3, 1), new DateOnly(2026, 5, 31)));
        Assert.Equal(HttpStatusCode.Forbidden, updateAttempt.StatusCode);

        var deleteAttempt = await athleteClient.DeleteAsync($"/api/v1/categories/{categoryId}/season-phases/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteAttempt.StatusCode);
    }

    [Fact]
    public async Task SP_10_Given_MultiplePhases_When_Listed_Then_ReturnedOrderedByStartDate()
    {
        var (_, categoryId) = await CreateCategoryForPfAAsync();

        var p3 = new CreateSeasonPhaseRequest("Transicion", SeasonPhaseType.Transition, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var p1 = new CreateSeasonPhaseRequest("Pretemporada", SeasonPhaseType.Preseason, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));
        var p2 = new CreateSeasonPhaseRequest("Competitiva", SeasonPhaseType.Competitive, new DateOnly(2026, 3, 1), new DateOnly(2026, 6, 30));

        // Insert in non-chronological order
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p3);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p1);
        await _clientPfA.PostAsJsonAsync($"/api/v1/categories/{categoryId}/season-phases", p2);

        var listRes = await _clientPfA.GetAsync($"/api/v1/categories/{categoryId}/season-phases");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var phases = await listRes.Content.ReadFromJsonAsync<List<SeasonPhaseDto>>();

        Assert.NotNull(phases);
        Assert.Equal(3, phases.Count);
        Assert.Equal("Pretemporada", phases[0].Name);
        Assert.Equal("Competitiva", phases[1].Name);
        Assert.Equal("Transicion", phases[2].Name);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
