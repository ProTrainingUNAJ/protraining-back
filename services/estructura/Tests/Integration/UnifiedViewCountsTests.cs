using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Estructura.Tests.Infrastructure;
using Xunit;

namespace Estructura.Tests.Integration;

public sealed class UnifiedViewCountsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;

    public UnifiedViewCountsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _pfBId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Given_OrganizationsAndCategories_When_ListOrganizations_Then_ReturnsAccurateCategoriesAndAthletesCount()
    {
        // 1. Create Organization 1 for PF A
        var org1Res = await _clientPfA.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest("Club Atletico Alpha", "Org con 2 categorias"));
        Assert.Equal(HttpStatusCode.Created, org1Res.StatusCode);
        var org1 = await org1Res.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org1);

        // 2. Create 2 categories for Org 1
        var cat1Res = await _clientPfA.PostAsJsonAsync("/api/v1/categories",
            new CreateCategoryRequest(org1.Id, "Primera", null));
        var cat2Res = await _clientPfA.PostAsJsonAsync("/api/v1/categories",
            new CreateCategoryRequest(org1.Id, "Reserva", null));
        var cat1 = await cat1Res.Content.ReadFromJsonAsync<CategoryDto>();
        var cat2 = await cat2Res.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat1);
        Assert.NotNull(cat2);

        // 3. Create 2 Athletes and assign to categories
        var athlete1Res = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Juan", "Perez", "juan@test.com", "11111111", false));
        var athlete2Res = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Carlos", "Gomez", "carlos@test.com", "22222222", false));
        Assert.Equal(HttpStatusCode.Created, athlete1Res.StatusCode);
        Assert.Equal(HttpStatusCode.Created, athlete2Res.StatusCode);

        var athlete1 = await athlete1Res.Content.ReadFromJsonAsync<AthleteDto>();
        var athlete2 = await athlete2Res.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete1);
        Assert.NotNull(athlete2);

        // Assign athlete 1 to cat1; athlete 2 to cat1 and cat2
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete1.Id}/categories/{cat1.Id}", null);
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete2.Id}/categories/{cat1.Id}", null);
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete2.Id}/categories/{cat2.Id}", null);

        // 4. Query Organizations list
        var listRes = await _clientPfA.GetAsync("/api/v1/organizations");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var orgs = await listRes.Content.ReadFromJsonAsync<List<OrganizationDto>>();
        Assert.NotNull(orgs);
        var foundOrg = orgs.FirstOrDefault(o => o.Id == org1.Id);
        Assert.NotNull(foundOrg);
        Assert.Equal(2, foundOrg.CategoriesCount);
        Assert.Equal(2, foundOrg.AthletesCount);

        // 5. Query Organization by ID
        var getOrgRes = await _clientPfA.GetAsync($"/api/v1/organizations/{org1.Id}");
        Assert.Equal(HttpStatusCode.OK, getOrgRes.StatusCode);
        var singleOrg = await getOrgRes.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(singleOrg);
        Assert.Equal(2, singleOrg.CategoriesCount);
        Assert.Equal(2, singleOrg.AthletesCount);
    }

    [Fact]
    public async Task Given_Categories_When_QueryWithAndWithoutOrganizationId_Then_ReturnsExpectedResultsAndAthletesCount()
    {
        // 1. Create Organization for PF A
        var orgRes = await _clientPfA.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest("Club Beta", null));
        var org = await orgRes.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);

        var catRes = await _clientPfA.PostAsJsonAsync("/api/v1/categories",
            new CreateCategoryRequest(org.Id, "Sub 20", null));
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);

        // Create athlete assigned to cat
        var athleteRes = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Atleta", "Uno", "atleta1@beta.com", "33333333", false));
        var athlete = await athleteRes.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat.Id}", null);

        // 2. Query categories with organizationId
        var queryWithOrgRes = await _clientPfA.GetAsync($"/api/v1/categories?organizationId={org.Id}");
        Assert.Equal(HttpStatusCode.OK, queryWithOrgRes.StatusCode);
        var catsWithOrg = await queryWithOrgRes.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(catsWithOrg);
        var foundCat = catsWithOrg.FirstOrDefault(c => c.Id == cat.Id);
        Assert.NotNull(foundCat);
        Assert.Equal(1, foundCat.AthletesCount);

        // 3. Query categories without organizationId (returns all categories for PF)
        var queryAllRes = await _clientPfA.GetAsync("/api/v1/categories");
        Assert.Equal(HttpStatusCode.OK, queryAllRes.StatusCode);
        var allCats = await queryAllRes.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(allCats);
        Assert.Contains(allCats, c => c.Id == cat.Id && c.AthletesCount == 1);
    }

    [Fact]
    public async Task Given_TwoPreparadoresFisicos_When_ListingOrganizationsAndCategories_Then_CountsAreIsolated()
    {
        // PF A creates an org and category
        var orgARes = await _clientPfA.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Org A", null));
        var orgA = await orgARes.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(orgA);
        await _clientPfA.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(orgA.Id, "Cat A", null));

        // PF B creates an org with no categories
        var orgBRes = await _clientPfB.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Org B", null));
        var orgB = await orgBRes.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(orgB);

        // Check PF B's org list: categoriesCount should be 0, does not leak A's category count
        var listBRes = await _clientPfB.GetAsync("/api/v1/organizations");
        var orgsB = await listBRes.Content.ReadFromJsonAsync<List<OrganizationDto>>();
        Assert.NotNull(orgsB);
        var singleOrgB = orgsB.First(o => o.Id == orgB.Id);
        Assert.Equal(0, singleOrgB.CategoriesCount);
        Assert.Equal(0, singleOrgB.AthletesCount);
    }
}
