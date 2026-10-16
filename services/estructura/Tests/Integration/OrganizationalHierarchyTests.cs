using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Estructura.Tests.Integration;

public sealed class OrganizationalHierarchyTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly HttpClient _clientPfA;

    public OrganizationalHierarchyTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
    }

    [Fact]
    public async Task Given_AuthenticatedPf_When_CreatesOrganizationAndCategories_Then_HierarchyPersistsAndCanBeRetrieved()
    {
        // 1. Create Organization
        var orgRequest = new CreateOrganizationRequest("Club Atletico Test", "Organizacion de prueba para jerarquia");
        var createOrgResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", orgRequest);
        Assert.Equal(HttpStatusCode.Created, createOrgResponse.StatusCode);

        var org = await createOrgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);
        Assert.Equal("Club Atletico Test", org.Name);

        // 2. Create Categories within Organization
        var cat1Request = new CreateCategoryRequest("Plantel Superior", "Primera division");
        var cat2Request = new CreateCategoryRequest("Juveniles M19", "Menores de 19");

        var cat1Response = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org.Id}", cat1Request);
        var cat2Response = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org.Id}", cat2Request);

        Assert.Equal(HttpStatusCode.Created, cat1Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, cat2Response.StatusCode);

        var cat1 = await cat1Response.Content.ReadFromJsonAsync<CategoryDto>();
        var cat2 = await cat2Response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat1);
        Assert.NotNull(cat2);
        Assert.Equal(org.Id, cat1.OrganizationId);
        Assert.Equal(org.Id, cat2.OrganizationId);

        // 3. Query Organizations list
        var listOrgResponse = await _clientPfA.GetAsync("/api/organizations");
        Assert.Equal(HttpStatusCode.OK, listOrgResponse.StatusCode);
        var orgs = await listOrgResponse.Content.ReadFromJsonAsync<List<OrganizationDto>>();
        Assert.NotNull(orgs);
        Assert.Contains(orgs, o => o.Id == org.Id);

        // 4. Query Categories list by Organization
        var listCatsResponse = await _clientPfA.GetAsync($"/api/categories?organizationId={org.Id}");
        Assert.Equal(HttpStatusCode.OK, listCatsResponse.StatusCode);
        var cats = await listCatsResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(cats);
        Assert.Equal(2, cats.Count);
        Assert.Contains(cats, c => c.Id == cat1.Id && c.Name == "Plantel Superior");
        Assert.Contains(cats, c => c.Id == cat2.Id && c.Name == "Juveniles M19");
    }

    [Fact]
    public async Task Given_AthleteAssociatedToMultipleCategories_When_QueryingAssociations_Then_ReturnsAllAuthorizedCategoriesWithoutDuplicatingAthlete()
    {
        // 1. Setup Organization and 2 Categories
        var orgResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club Rugby Test", null));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);

        var cat1Response = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org.Id}", new CreateCategoryRequest("Primera A", null));
        var cat2Response = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org.Id}", new CreateCategoryRequest("Intermedia", null));
        var cat1 = await cat1Response.Content.ReadFromJsonAsync<CategoryDto>();
        var cat2 = await cat2Response.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat1);
        Assert.NotNull(cat2);

        // 2. Create Athlete
        var athleteRequest = new CreateAthleteRequest(
            FirstName: "Martin",
            LastName: "Rodriguez",
            Email: "martin.rodriguez@test.com",
            Phone: "+5491112345678",
            IsMinor: false);
        var athleteResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", athleteRequest);
        Assert.Equal(HttpStatusCode.Created, athleteResponse.StatusCode);
        var athlete = await athleteResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        // 3. Associate Athlete to both Categories (N:N)
        var assign1 = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat1.Id}", null);
        var assign2 = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat2.Id}", null);
        Assert.Equal(HttpStatusCode.OK, assign1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, assign2.StatusCode);

        // 4. Query Categories of Athlete -> returns exactly both categories
        var athleteCatsResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete.Id}/categories");
        Assert.Equal(HttpStatusCode.OK, athleteCatsResponse.StatusCode);
        var athleteCats = await athleteCatsResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(athleteCats);
        Assert.Equal(2, athleteCats.Count);
        Assert.Contains(athleteCats, c => c.Id == cat1.Id);
        Assert.Contains(athleteCats, c => c.Id == cat2.Id);

        // 5. Query Athlete list -> must not duplicate athlete in list
        var listAthletesResponse = await _clientPfA.GetAsync("/api/v1/athletes");
        var athletes = await listAthletesResponse.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(athletes);
        Assert.Single(athletes, a => a.Id == athlete.Id);
    }

    [Fact]
    public async Task Given_MultipleAthletesInSameCategory_When_ListingAthletesByCategory_Then_ReturnsAllAssociatedAthletes()
    {
        // 1. Setup Organization and Category
        var orgResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club Hockey", null));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catResponse = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org!.Id}", new CreateCategoryRequest("Sub 16", null));
        var cat = await catResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);

        // 2. Create 2 Athletes
        var a1Response = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("Sofia", "Alvarez", "sofia@test.com", "12345", false));
        var a2Response = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("Camila", "Benitez", "camila@test.com", "54321", false));
        var a1 = await a1Response.Content.ReadFromJsonAsync<AthleteDto>();
        var a2 = await a2Response.Content.ReadFromJsonAsync<AthleteDto>();

        // 3. Associate both to Category
        await _clientPfA.PostAsync($"/api/v1/athletes/{a1!.Id}/categories/{cat.Id}", null);
        await _clientPfA.PostAsync($"/api/v1/athletes/{a2!.Id}/categories/{cat.Id}", null);

        // 4. Query Athletes in Category
        var response = await _clientPfA.GetAsync($"/api/categories/{cat.Id}/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var athletesInCat = await response.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(athletesInCat);
        Assert.Equal(2, athletesInCat.Count);
        Assert.Contains(athletesInCat, a => a.Id == a1.Id);
        Assert.Contains(athletesInCat, a => a.Id == a2.Id);
    }

    [Fact]
    public async Task Given_ExistingAssociation_When_AssociatingSameAthleteAndCategoryAgain_Then_OperationIsIdempotentAndNoDuplicatesCreated()
    {
        // 1. Setup
        var orgResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club Idempotencia", null));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catResponse = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org!.Id}", new CreateCategoryRequest("Cat Unica", null));
        var cat = await catResponse.Content.ReadFromJsonAsync<CategoryDto>();
        var athleteResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("Tomas", "Diaz", "tomas@test.com", "111", false));
        var athlete = await athleteResponse.Content.ReadFromJsonAsync<AthleteDto>();

        // 2. Associate first time
        var firstAssign = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete!.Id}/categories/{cat!.Id}", null);
        Assert.Equal(HttpStatusCode.OK, firstAssign.StatusCode);

        // 3. Associate second time (idempotency check)
        var secondAssign = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat.Id}", null);
        Assert.Equal(HttpStatusCode.OK, secondAssign.StatusCode);

        // 4. Verify in DB and via endpoint that only 1 link exists
        var athleteCatsResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete.Id}/categories");
        var athleteCats = await athleteCatsResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(athleteCats);
        Assert.Single(athleteCats);

        using var dbContext = _factory.CreateDbContext();
        var linksInDb = await dbContext.AthleteCategories
            .IgnoreQueryFilters()
            .Where(ac => ac.AthleteId == athlete.Id && ac.CategoryId == cat.Id && ac.DeletedAt == null)
            .ToListAsync();
        Assert.Single(linksInDb);
    }

    [Fact]
    public async Task Given_AthleteWithMultipleCategories_When_RemovingOneCategory_Then_AthleteIsNotDeletedAndOtherCategoriesRemain()
    {
        // 1. Setup Org, 2 Categories, 1 Athlete
        var orgResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club Desasociacion", null));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var cat1Response = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org!.Id}", new CreateCategoryRequest("Cat Primaria", null));
        var cat2Response = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={org.Id}", new CreateCategoryRequest("Cat Secundaria", null));
        var cat1 = await cat1Response.Content.ReadFromJsonAsync<CategoryDto>();
        var cat2 = await cat2Response.Content.ReadFromJsonAsync<CategoryDto>();
        var athleteResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("Valentin", "Gimenez", "valentin@test.com", "222", false));
        var athlete = await athleteResponse.Content.ReadFromJsonAsync<AthleteDto>();

        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete!.Id}/categories/{cat1!.Id}", null);
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat2!.Id}", null);

        // 2. Remove Cat1 association
        var removeResponse = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat1.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        // 3. Verify only Cat2 remains for Athlete
        var catsResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete.Id}/categories");
        var cats = await catsResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(cats);
        Assert.Single(cats);
        Assert.Equal(cat2.Id, cats[0].Id);

        // 4. Verify Athlete still exists and is not deleted
        var getAthleteResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete.Id}");
        Assert.Equal(HttpStatusCode.OK, getAthleteResponse.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _clientPfA.Dispose();
        await _factory.DisposeAsync();
    }
}
