using System.Net;
using System.Net.Http.Json;
using System.Text;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Estructura.Tests.Integration;

public sealed class MultitenantIsolationTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;
    private readonly HttpClient _unauthenticatedClient;

    public MultitenantIsolationTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _pfBId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);
        _unauthenticatedClient = _factory.CreateClient();
    }

    [Fact]
    public async Task Given_NoAuthenticationOrInvalidToken_When_AccessingProtectedEndpoints_Then_Returns401Unauthorized()
    {
        // 1. Unauthenticated requests
        var unauthOrgs = await _unauthenticatedClient.GetAsync("/api/organizations");
        var unauthCats = await _unauthenticatedClient.GetAsync("/api/categories?organizationId=" + Guid.NewGuid());
        var unauthAthletes = await _unauthenticatedClient.GetAsync("/api/v1/athletes");

        Assert.Equal(HttpStatusCode.Unauthorized, unauthOrgs.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthCats.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthAthletes.StatusCode);

        // 2. Expired token
        var expiredClient = _factory.CreateClient();
        expiredClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestJwtGenerator.GenerateExpiredToken(_pfAId));
        var expiredResponse = await expiredClient.GetAsync("/api/organizations");
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);

        // 3. Invalid signature key token
        var invalidKeyClient = _factory.CreateClient();
        invalidKeyClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestJwtGenerator.GenerateTokenWithInvalidKey(_pfAId));
        var invalidKeyResponse = await invalidKeyClient.GetAsync("/api/organizations");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidKeyResponse.StatusCode);
    }

    [Fact]
    public async Task Given_OrganizationOwnedByPfB_When_PfAConsultsOrLists_Then_PfADoesNotSeePfBOrganizationAndGets404OnDirectAccess()
    {
        // 1. PF-B creates an organization
        var orgBResponse = await _clientPfB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Organizacion Secreta PF-B", "Privada"));
        Assert.Equal(HttpStatusCode.Created, orgBResponse.StatusCode);
        var orgB = await orgBResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(orgB);

        // 2. PF-A lists organizations -> must not contain Org-B
        var listResponse = await _clientPfA.GetAsync("/api/organizations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var orgsPfA = await listResponse.Content.ReadFromJsonAsync<List<OrganizationDto>>();
        Assert.NotNull(orgsPfA);
        Assert.DoesNotContain(orgsPfA, o => o.Id == orgB.Id);

        // 3. PF-A queries Org-B by Id directly -> returns 404 Not Found (without revealing existence)
        var directGetResponse = await _clientPfA.GetAsync($"/api/organizations/{orgB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, directGetResponse.StatusCode);
    }

    [Fact]
    public async Task Given_OrganizationOwnedByPfB_When_PfAAttemptsUpdateOrDelete_Then_Returns404AndDataRemainsUnchangedInDb()
    {
        // 1. PF-B creates Org-B
        var orgBResponse = await _clientPfB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Org Original B", "Descripcion B"));
        var orgB = await orgBResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(orgB);

        // 2. PF-A attempts to update Org-B -> must return 404
        var updateResponse = await _clientPfA.PutAsJsonAsync(
            $"/api/organizations/{orgB.Id}",
            new UpdateOrganizationRequest("Nombre Modificado Ilegalmente", "Hack"));
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        // 3. PF-A attempts to delete Org-B -> must return 404
        var deleteResponse = await _clientPfA.DeleteAsync($"/api/organizations/{orgB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        // 4. Verify in DB and from PF-B that Org-B is completely intact
        using var dbContext = _factory.CreateDbContext();
        var orgInDb = await dbContext.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orgB.Id);

        Assert.NotNull(orgInDb);
        Assert.Equal("Org Original B", orgInDb.Name);
        Assert.Equal("Descripcion B", orgInDb.Description);
        Assert.Null(orgInDb.DeletedAt);

        var verifyFromPfB = await _clientPfB.GetAsync($"/api/organizations/{orgB.Id}");
        Assert.Equal(HttpStatusCode.OK, verifyFromPfB.StatusCode);
    }

    [Fact]
    public async Task Given_OrganizationOwnedByPfB_When_PfAAttemptsToCreateCategoryInsideIt_Then_Returns404AndCategoryIsNotCreated()
    {
        // 1. PF-B creates Org-B
        var orgBResponse = await _clientPfB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club PF-B", null));
        var orgB = await orgBResponse.Content.ReadFromJsonAsync<OrganizationDto>();

        // 2. PF-A attempts to create category in Org-B -> must return 404 (organization not found in PF-A scope)
        var createCatResponse = await _clientPfA.PostAsJsonAsync(
            $"/api/categories?organizationId={orgB!.Id}",
            new CreateCategoryRequest("Categoria Ilegal", "Intruso"));
        Assert.Equal(HttpStatusCode.NotFound, createCatResponse.StatusCode);

        // 3. Verify in DB that no category was created
        using var dbContext = _factory.CreateDbContext();
        var catInDb = await dbContext.Categories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.OrganizationId == orgB.Id && c.Name == "Categoria Ilegal");
        Assert.Null(catInDb);
    }

    [Fact]
    public async Task Given_CategoryOwnedByPfB_When_PfAAccessesModifiesOrDeletesIt_Then_Returns404AndCategoryRemainsIntact()
    {
        // 1. PF-B creates Org-B and Cat-B
        var orgBResponse = await _clientPfB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club B", null));
        var orgB = await orgBResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catBResponse = await _clientPfB.PostAsJsonAsync($"/api/categories?organizationId={orgB!.Id}", new CreateCategoryRequest("Sub 21 PF-B", "Legitima"));
        var catB = await catBResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(catB);

        // 2. PF-A lists categories in Org-B -> empty list
        var listResponse = await _clientPfA.GetAsync($"/api/categories?organizationId={orgB.Id}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var cats = await listResponse.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(cats);
        Assert.Empty(cats);

        // 3. PF-A queries Cat-B by Id -> 404
        var getResponse = await _clientPfA.GetAsync($"/api/categories/{catB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // 4. PF-A updates Cat-B -> 404
        var putResponse = await _clientPfA.PutAsJsonAsync($"/api/categories/{catB.Id}", new UpdateCategoryRequest("Hacked Cat", null));
        Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);

        // 5. PF-A deletes Cat-B -> 404
        var delResponse = await _clientPfA.DeleteAsync($"/api/categories/{catB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delResponse.StatusCode);

        // 6. Verify Cat-B persists untouched
        using var dbContext = _factory.CreateDbContext();
        var catInDb = await dbContext.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == catB.Id);
        Assert.NotNull(catInDb);
        Assert.Equal("Sub 21 PF-B", catInDb.Name);
        Assert.Null(catInDb.DeletedAt);
    }

    [Fact]
    public async Task Given_AthleteOwnedByPfB_When_PfAConsultsModifiesOrDeletesIt_Then_Returns404AndAthleteRemainsIntact()
    {
        // 1. PF-B creates Athlete-B
        var athleteBResponse = await _clientPfB.PostAsJsonAsync(
            "/api/v1/athletes",
            new CreateAthleteRequest("Esteban", "Brito", "esteban.b@test.com", "123456", false));
        var athleteB = await athleteBResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athleteB);

        // 2. PF-A lists athletes -> must not contain Athlete-B
        var listResponse = await _clientPfA.GetAsync("/api/v1/athletes");
        var athletes = await listResponse.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(athletes);
        Assert.DoesNotContain(athletes, a => a.Id == athleteB.Id);

        // 3. PF-A queries Athlete-B by known GUID -> 404
        var getResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athleteB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // 4. PF-A attempts to update habits permission -> 404
        var patchResponse = await _clientPfA.PatchAsJsonAsync($"/api/v1/athletes/{athleteB.Id}/habits-permission", new { allowsHabitsRecommendations = true });
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);

        // 5. PF-A attempts to delete Athlete-B -> 404
        var delResponse = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athleteB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delResponse.StatusCode);

        // 6. Verify DB integrity
        using var dbContext = _factory.CreateDbContext();
        var athleteInDb = await dbContext.Athletes.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == athleteB.Id);
        Assert.NotNull(athleteInDb);
        Assert.Null(athleteInDb.DeletedAt);
    }

    [Fact]
    public async Task Given_CrossTenantAssociationAttempts_When_PfAAttemptsToLinkOrUnlinkPfBAthletesOrCategories_Then_OperationRejectedWith404()
    {
        // 1. Setup PF-A assets
        var orgAResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club A", null));
        var orgA = await orgAResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catAResponse = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={orgA!.Id}", new CreateCategoryRequest("Cat A", null));
        var catA = await catAResponse.Content.ReadFromJsonAsync<CategoryDto>();
        var athleteAResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("Atleta", "A", "a@test.com", "111", false));
        var athleteA = await athleteAResponse.Content.ReadFromJsonAsync<AthleteDto>();

        // 2. Setup PF-B assets
        var orgBResponse = await _clientPfB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club B", null));
        var orgB = await orgBResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catBResponse = await _clientPfB.PostAsJsonAsync($"/api/categories?organizationId={orgB!.Id}", new CreateCategoryRequest("Cat B", null));
        var catB = await catBResponse.Content.ReadFromJsonAsync<CategoryDto>();
        var athleteBResponse = await _clientPfB.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("Atleta", "B", "b@test.com", "222", false));
        var athleteB = await athleteBResponse.Content.ReadFromJsonAsync<AthleteDto>();

        // PF-B links Athlete-B to Cat-B
        var linkBResponse = await _clientPfB.PostAsync($"/api/v1/athletes/{athleteB!.Id}/categories/{catB!.Id}", null);
        Assert.Equal(HttpStatusCode.OK, linkBResponse.StatusCode);

        // 3. Cross-Tenant Attempt 1: PF-A attempts to link own athlete to PF-B's category -> 404 (category not found)
        var crossLink1 = await _clientPfA.PostAsync($"/api/v1/athletes/{athleteA!.Id}/categories/{catB.Id}", null);
        Assert.Equal(HttpStatusCode.NotFound, crossLink1.StatusCode);

        // 4. Cross-Tenant Attempt 2: PF-A attempts to link PF-B's athlete to own category -> 404 (athlete not found)
        var crossLink2 = await _clientPfA.PostAsync($"/api/v1/athletes/{athleteB.Id}/categories/{catA!.Id}", null);
        Assert.Equal(HttpStatusCode.NotFound, crossLink2.StatusCode);

        // 5. Cross-Tenant Attempt 3: PF-A attempts to unlink PF-B's association -> 404
        var crossUnlink = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athleteB.Id}/categories/{catB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossUnlink.StatusCode);

        // 6. Verify in DB that PF-B's association still exists and no cross links exist
        using var dbContext = _factory.CreateDbContext();
        var linkBInDb = await dbContext.AthleteCategories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ac => ac.AthleteId == athleteB.Id && ac.CategoryId == catB.Id);
        Assert.NotNull(linkBInDb);
        Assert.Null(linkBInDb.DeletedAt);

        var crossLinkInDb = await dbContext.AthleteCategories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ac => (ac.AthleteId == athleteA.Id && ac.CategoryId == catB.Id)
                                    || (ac.AthleteId == athleteB.Id && ac.CategoryId == catA.Id));
        Assert.Null(crossLinkInDb);
    }

    [Fact]
    public async Task Given_PayloadWithTamperedPfId_When_PfACreatesResource_Then_AppDbContextForcesAuthenticatedPfId()
    {
        // PF-A sends JSON with explicit "pfId" targeting PF-B
        var jsonWithForeignPfId = $$"""
        {
            "name": "Org Con PfId Adulterado",
            "description": "Intento de inyeccion",
            "pfId": "{{_pfBId}}"
        }
        """;

        var content = new StringContent(jsonWithForeignPfId, Encoding.UTF8, "application/json");
        var response = await _clientPfA.PostAsync("/api/organizations", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdOrg = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(createdOrg);

        // Direct DB inspection: PfId MUST be _pfAId from token, NOT _pfBId
        using var dbContext = _factory.CreateDbContext();
        var orgInDb = await dbContext.Organizations
            .IgnoreQueryFilters()
            .FirstAsync(o => o.Id == createdOrg.Id);

        Assert.Equal(_pfAId, orgInDb.PfId);
        Assert.NotEqual(_pfBId, orgInDb.PfId);
    }

    [Fact]
    public async Task Given_CrossTenantDataInDatabase_When_QueryingWithJoinsAndIncludes_Then_NoDataLeakedAcrossTenants()
    {
        // 1. Setup PF-A assets
        var orgAResponse = await _clientPfA.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club A", null));
        var orgA = await orgAResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catAResponse = await _clientPfA.PostAsJsonAsync($"/api/categories?organizationId={orgA!.Id}", new CreateCategoryRequest("Cat A", null));
        var catA = await catAResponse.Content.ReadFromJsonAsync<CategoryDto>();
        var aAResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("AtletaA", "Test", "a@test.com", "111", false));
        var aA = await aAResponse.Content.ReadFromJsonAsync<AthleteDto>();
        await _clientPfA.PostAsync($"/api/v1/athletes/{aA!.Id}/categories/{catA!.Id}", null);

        // 2. Setup PF-B assets in same shared database
        var orgBResponse = await _clientPfB.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest("Club B", null));
        var orgB = await orgBResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        var catBResponse = await _clientPfB.PostAsJsonAsync($"/api/categories?organizationId={orgB!.Id}", new CreateCategoryRequest("Cat B", null));
        var catB = await catBResponse.Content.ReadFromJsonAsync<CategoryDto>();
        var aBResponse = await _clientPfB.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest("AtletaB", "Test", "b@test.com", "222", false));
        var aB = await aBResponse.Content.ReadFromJsonAsync<AthleteDto>();
        await _clientPfB.PostAsync($"/api/v1/athletes/{aB!.Id}/categories/{catB!.Id}", null);

        // 3. Query athletes by category with joins (PF-A)
        var athletesInCatA = await _clientPfA.GetFromJsonAsync<List<AthleteDto>>($"/api/categories/{catA.Id}/athletes");
        Assert.NotNull(athletesInCatA);
        Assert.Single(athletesInCatA);
        Assert.Equal(aA.Id, athletesInCatA[0].Id);
        Assert.DoesNotContain(athletesInCatA, a => a.Id == aB.Id);

        // 4. Query categories by athlete with joins (PF-B)
        var catsOfAthleteB = await _clientPfB.GetFromJsonAsync<List<CategoryDto>>($"/api/v1/athletes/{aB.Id}/categories");
        Assert.NotNull(catsOfAthleteB);
        Assert.Single(catsOfAthleteB);
        Assert.Equal(catB.Id, catsOfAthleteB[0].Id);
        Assert.DoesNotContain(catsOfAthleteB, c => c.Id == catA.Id);
    }

    public async ValueTask DisposeAsync()
    {
        _clientPfA.Dispose();
        _clientPfB.Dispose();
        _unauthenticatedClient.Dispose();
        await _factory.DisposeAsync();
    }
}
