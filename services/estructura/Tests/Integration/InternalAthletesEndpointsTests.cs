using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estructura.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class InternalAthletesEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;
    private readonly HttpClient _serviceClient;
    private readonly HttpClient _unauthenticatedClient;

    public InternalAthletesEndpointsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _pfBId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);
        _serviceClient = _factory.CreateAuthenticatedClient(Guid.NewGuid(), role: "Servicio");
        _unauthenticatedClient = _factory.CreateClient();
    }

    [Fact]
    public async Task Given_NoOrInvalidRole_When_AccessingInternalAthletes_Then_EnforcesServicioRole()
    {
        // 1. Unauthenticated -> 401
        var unauthResponse = await _unauthenticatedClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        // 2. PreparadorFisico role -> 403 Forbidden
        var pfResponse = await _clientPfA.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.Forbidden, pfResponse.StatusCode);

        // 3. Servicio role -> 200 OK
        var serviceResponse = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, serviceResponse.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteWithOneCategory_When_GetActiveAthletes_Then_ReturnsCategoryIdAndSingleCategoryIds()
    {
        // Arrange
        var org = await CreateOrganizationAsync(_clientPfA, "Club Atletico Unit");
        var cat = await CreateCategoryAsync(_clientPfA, org.Id, "Sub-20");
        var athlete = await CreateAthleteAsync(_clientPfA, "Lionel", "Messi");

        var assignRes = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat.Id}", null);
        Assert.Equal(HttpStatusCode.OK, assignRes.StatusCode);

        // Act
        var response = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var athletes = await response.Content.ReadFromJsonAsync<List<InternalAthleteSummaryDto>>();
        Assert.NotNull(athletes);

        var match = athletes.FirstOrDefault(a => a.Id == athlete.Id);
        Assert.NotNull(match);
        Assert.Equal(_pfAId, match.PfId);
        Assert.Equal(cat.Id, match.CategoryId);
        Assert.NotNull(match.CategoryIds);
        Assert.Single(match.CategoryIds!);
        Assert.Equal(cat.Id, match.CategoryIds![0]);
        Assert.True(match.Active);
    }

    [Fact]
    public async Task Given_AthleteWithMultipleCategories_When_GetActiveAthletes_Then_ReturnsDeterministicCategoryIdAndAllCategoryIds()
    {
        // Arrange
        var org = await CreateOrganizationAsync(_clientPfA, "Club Atletico Multi");
        var cat1 = await CreateCategoryAsync(_clientPfA, org.Id, "Categoria A");
        var cat2 = await CreateCategoryAsync(_clientPfA, org.Id, "Categoria B");
        var athlete = await CreateAthleteAsync(_clientPfA, "Angel", "Di Maria");

        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat1.Id}", null);
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat2.Id}", null);

        // Act
        var response = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var athletes = await response.Content.ReadFromJsonAsync<List<InternalAthleteSummaryDto>>();
        Assert.NotNull(athletes);

        var match = athletes.FirstOrDefault(a => a.Id == athlete.Id);
        Assert.NotNull(match);
        Assert.Equal(_pfAId, match.PfId);
        Assert.NotNull(match.CategoryIds);
        Assert.Equal(2, match.CategoryIds!.Count);
        Assert.Contains(cat1.Id, match.CategoryIds!);
        Assert.Contains(cat2.Id, match.CategoryIds!);

        // Selection for CategoryId is deterministic (matches the first category in CategoryIds)
        Assert.Equal(match.CategoryIds![0], match.CategoryId);
    }

    [Fact]
    public async Task Given_AthleteWithoutCategories_When_GetActiveAthletes_Then_ReturnsNullCategoryIdAndEmptyCategoryIds()
    {
        // Arrange
        var athlete = await CreateAthleteAsync(_clientPfA, "Julian", "Alvarez");

        // Act
        var response = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var athletes = await response.Content.ReadFromJsonAsync<List<InternalAthleteSummaryDto>>();
        Assert.NotNull(athletes);

        var match = athletes.FirstOrDefault(a => a.Id == athlete.Id);
        Assert.NotNull(match);
        Assert.Null(match.CategoryId);
        Assert.NotNull(match.CategoryIds);
        Assert.Empty(match.CategoryIds!);
    }

    [Fact]
    public async Task Given_DetachedOrDeletedCategory_When_GetActiveAthletes_Then_ExcludesDeletedCategories()
    {
        // Arrange: Cat1 and Cat2 assigned to Athlete
        var org = await CreateOrganizationAsync(_clientPfA, "Club SoftDelete");
        var cat1 = await CreateCategoryAsync(_clientPfA, org.Id, "Cat Unassigned");
        var cat2 = await CreateCategoryAsync(_clientPfA, org.Id, "Cat Deleted");
        var athlete = await CreateAthleteAsync(_clientPfA, "Emiliano", "Martinez");

        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat1.Id}", null);
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat2.Id}", null);

        // 1. Unassign Cat1 via API (marks AthleteCategory as deleted)
        var unassignRes = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat1.Id}");
        Assert.Equal(HttpStatusCode.NoContent, unassignRes.StatusCode);

        // 2. Soft-delete Cat2 in database directly to test Category.DeletedAt filter
        using (var db = _factory.CreateDbContext(_pfAId))
        {
            var categoryToSoftDelete = await db.Categories.FindAsync(cat2.Id);
            Assert.NotNull(categoryToSoftDelete);
            db.Categories.Remove(categoryToSoftDelete);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var athletes = await response.Content.ReadFromJsonAsync<List<InternalAthleteSummaryDto>>();
        Assert.NotNull(athletes);

        var match = athletes.FirstOrDefault(a => a.Id == athlete.Id);
        Assert.NotNull(match);
        Assert.Null(match.CategoryId);
        Assert.NotNull(match.CategoryIds);
        Assert.Empty(match.CategoryIds!);
    }

    [Fact]
    public async Task Given_MultiplePfs_When_GetActiveAthletes_Then_IsolatesCategoriesPerPf()
    {
        // Arrange
        var orgA = await CreateOrganizationAsync(_clientPfA, "Club PF A");
        var catA = await CreateCategoryAsync(_clientPfA, orgA.Id, "Primera PF A");
        var athleteA = await CreateAthleteAsync(_clientPfA, "Atleta", "De A");
        await _clientPfA.PostAsync($"/api/v1/athletes/{athleteA.Id}/categories/{catA.Id}", null);

        var orgB = await CreateOrganizationAsync(_clientPfB, "Club PF B");
        var catB = await CreateCategoryAsync(_clientPfB, orgB.Id, "Primera PF B");
        var athleteB = await CreateAthleteAsync(_clientPfB, "Atleta", "De B");
        await _clientPfB.PostAsync($"/api/v1/athletes/{athleteB.Id}/categories/{catB.Id}", null);

        // Act
        var response = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var athletes = await response.Content.ReadFromJsonAsync<List<InternalAthleteSummaryDto>>();
        Assert.NotNull(athletes);

        var matchA = athletes.FirstOrDefault(a => a.Id == athleteA.Id);
        Assert.NotNull(matchA);
        Assert.Equal(_pfAId, matchA.PfId);
        Assert.Equal(catA.Id, matchA.CategoryId);
        Assert.Contains(catA.Id, matchA.CategoryIds!);
        Assert.DoesNotContain(catB.Id, matchA.CategoryIds!);

        var matchB = athletes.FirstOrDefault(a => a.Id == athleteB.Id);
        Assert.NotNull(matchB);
        Assert.Equal(_pfBId, matchB.PfId);
        Assert.Equal(catB.Id, matchB.CategoryId);
        Assert.Contains(catB.Id, matchB.CategoryIds!);
        Assert.DoesNotContain(catA.Id, matchB.CategoryIds!);
    }

    private sealed record LegacyInternalAthleteSummaryDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("pfId")] Guid PfId,
        [property: JsonPropertyName("categoryId")] Guid? CategoryId,
        [property: JsonPropertyName("active")] bool Active);

    [Fact]
    public async Task Given_LegacyConsumerWithoutCategoryIds_When_Deserializing_Then_MaintainsBackwardsCompatibility()
    {
        // Arrange
        var org = await CreateOrganizationAsync(_clientPfA, "Club Legacy");
        var cat = await CreateCategoryAsync(_clientPfA, org.Id, "Sub-18");
        var athlete = await CreateAthleteAsync(_clientPfA, "Rodrigo", "De Paul");
        await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat.Id}", null);

        // Act
        var response = await _serviceClient.GetAsync("/api/v1/internal/athletes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"categoryIds\"", rawJson);
        Assert.Contains("\"categoryId\"", rawJson);

        // Deserialize using legacy DTO that knows nothing about CategoryIds
        var legacyAthletes = await response.Content.ReadFromJsonAsync<List<LegacyInternalAthleteSummaryDto>>();
        Assert.NotNull(legacyAthletes);

        var match = legacyAthletes.FirstOrDefault(a => a.Id == athlete.Id);
        Assert.NotNull(match);
        Assert.Equal(_pfAId, match.PfId);
        Assert.Equal(cat.Id, match.CategoryId);
        Assert.True(match.Active);
    }

    private static async Task<OrganizationDto> CreateOrganizationAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest(name, null));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var org = await res.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);
        return org;
    }

    private static async Task<CategoryDto> CreateCategoryAsync(HttpClient client, Guid orgId, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(orgId, name, null));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var cat = await res.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);
        return cat;
    }

    private static async Task<AthleteDto> CreateAthleteAsync(HttpClient client, string firstName, string lastName)
    {
        var res = await client.PostAsJsonAsync("/api/v1/athletes", new CreateAthleteRequest(
            FirstName: firstName,
            LastName: lastName,
            Email: $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}@example.com",
            Phone: "+5491112345678",
            IsMinor: false));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var athlete = await res.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);
        return athlete;
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
