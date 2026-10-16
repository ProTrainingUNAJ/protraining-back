using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Estructura.Tests.Integration;

public sealed class AthleteSoftDeleteOutboxTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId;
    private readonly Guid _pfBId;
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;

    public AthleteSoftDeleteOutboxTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfAId = Guid.NewGuid();
        _pfBId = Guid.NewGuid();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);
    }

    [Fact]
    public async Task Given_AuthorizedPf_When_DeletingAthlete_Then_PerformsSoftDeleteAndRegistersPendingPurgeInSingleTransaction()
    {
        // 1. Arrange: Create Athlete with PF-A
        var request = new CreateAthleteRequest(
            FirstName: "Lucas",
            LastName: "Vazquez",
            Email: "lucas.vazquez@test.com",
            Phone: "+5491111223344",
            IsMinor: false);

        var createResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var athlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        // 2. Act: Delete Athlete
        var deleteResponse = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // 3. Assert - Active Queries (CA-05)
        var getResponse = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await _clientPfA.GetAsync("/api/v1/athletes");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var activeAthletes = await listResponse.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(activeAthletes);
        Assert.DoesNotContain(activeAthletes, a => a.Id == athlete.Id);

        // 4. Assert - Direct Database State (CA-01, CA-02, CA-03)
        using var dbContext = _factory.CreateDbContext();

        // CA-01: Record is physically preserved, but deleted_at is set
        var dbAthlete = await dbContext.Athletes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == athlete.Id);

        Assert.NotNull(dbAthlete);
        Assert.NotNull(dbAthlete.DeletedAt);
        Assert.True(dbAthlete.DeletedAt <= DateTime.UtcNow);
        Assert.Equal(_pfAId, dbAthlete.PfId);

        // CA-02: Pending purge registered in pending_purges with minimal technical data
        var pendingPurges = await dbContext.PendingPurges
            .IgnoreQueryFilters()
            .Where(p => p.AthleteId == athlete.Id)
            .ToListAsync();

        Assert.Single(pendingPurges);
        var purge = pendingPurges[0];
        Assert.Equal(athlete.Id, purge.AthleteId);
        Assert.Equal("Athlete", purge.EntityType);
        Assert.Equal(PendingPurge.PendingStatus, purge.Status);
        Assert.Equal(_pfAId, purge.PfId);
        Assert.Null(purge.ProcessedAt);
        Assert.True(purge.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Given_AthleteOwnedByPfB_When_PfAAttemptsToDelete_Then_Returns404AndDoesNotRegisterPendingPurge()
    {
        // 1. Arrange: Create Athlete with PF-B
        var request = new CreateAthleteRequest(
            FirstName: "Federico",
            LastName: "Mancuello",
            Email: "federico.mancuello@test.com",
            Phone: "+5491199887766",
            IsMinor: false);

        var createResponse = await _clientPfB.PostAsJsonAsync("/api/v1/athletes", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var athleteB = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athleteB);

        // 2. Act: PF-A tries to delete PF-B's Athlete (Cross-tenant attack)
        var crossDeleteResponse = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athleteB.Id}");

        // 3. Assert: 404 Not Found (CA-04)
        Assert.Equal(HttpStatusCode.NotFound, crossDeleteResponse.StatusCode);

        // Verify Database State: Athlete B is untouched, and NO purge is created
        using var dbContext = _factory.CreateDbContext();

        var dbAthleteB = await dbContext.Athletes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == athleteB.Id);

        Assert.NotNull(dbAthleteB);
        Assert.Null(dbAthleteB.DeletedAt); // Still active!

        var purgeRecords = await dbContext.PendingPurges
            .IgnoreQueryFilters()
            .Where(p => p.AthleteId == athleteB.Id)
            .ToListAsync();

        Assert.Empty(purgeRecords); // No purge record generated
    }

    [Fact]
    public async Task Given_SoftDeletedAthlete_When_DeletingAgain_Then_Returns404AndDoesNotCreateDuplicatePendingPurge()
    {
        // 1. Arrange: Create Athlete with PF-A
        var request = new CreateAthleteRequest(
            FirstName: "Rodrigo",
            LastName: "Battaglia",
            Email: "rodrigo.battaglia@test.com",
            Phone: "+5491144556677",
            IsMinor: false);

        var createResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", request);
        var athlete = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        // First deletion -> 204 NoContent
        var firstDelete = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete.Id}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        // 2. Act: Second deletion attempt (CA-06 Idempotency)
        var secondDelete = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete.Id}");

        // 3. Assert: 404 Not Found
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);

        // Verify Database State: Exactly one purge record exists, not two
        using var dbContext = _factory.CreateDbContext();
        var purgeCount = await dbContext.PendingPurges
            .IgnoreQueryFilters()
            .CountAsync(p => p.AthleteId == athlete.Id);

        Assert.Equal(1, purgeCount);
    }

    [Fact]
    public async Task Given_AthleteAssociatedToCategories_When_SoftDeleted_Then_IntermediateRelationsArePreservedAndCategoryExcludesAthlete()
    {
        // 1. Arrange: Setup Org, Category, and Athlete with PF-A
        var orgResponse = await _clientPfA.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest("Club Atletico Olimpia", "Club deportivo"));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);

        var catResponse = await _clientPfA.PostAsJsonAsync($"/api/v1/categories?organizationId={org.Id}",
            new CreateCategoryRequest("Categoria Primera", "Primera division"));
        var cat = await catResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);

        var athleteResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Ezequiel", "Barco", "ezequiel.barco@test.com", "+5491155667788", false));
        var athlete = await athleteResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);

        // Associate Athlete with Category (N:N)
        var assignResponse = await _clientPfA.PostAsync($"/api/v1/athletes/{athlete.Id}/categories/{cat.Id}", null);
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        // Verify active list in Category contains athlete
        var beforeList = await _clientPfA.GetAsync($"/api/v1/categories/{cat.Id}/athletes");
        var athletesInCatBefore = await beforeList.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(athletesInCatBefore);
        Assert.Contains(athletesInCatBefore, a => a.Id == athlete.Id);

        // 2. Act: Soft delete athlete
        var deleteResponse = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // 3. Assert: Active Category Athletes query excludes the soft-deleted athlete (CA-05)
        var afterList = await _clientPfA.GetAsync($"/api/v1/categories/{cat.Id}/athletes");
        var athletesInCatAfter = await afterList.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(athletesInCatAfter);
        Assert.DoesNotContain(athletesInCatAfter, a => a.Id == athlete.Id);

        // 4. Assert: Database intermediate row in athlete_categories remains physically intact (CA-07)
        using var dbContext = _factory.CreateDbContext();

        var relationExists = await dbContext.AthleteCategories
            .IgnoreQueryFilters()
            .AnyAsync(ac => ac.AthleteId == athlete.Id && ac.CategoryId == cat.Id);

        Assert.True(relationExists, "The N:N association in athlete_categories must physically remain intact.");

        var dbAthlete = await dbContext.Athletes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == athlete.Id);
        Assert.NotNull(dbAthlete?.DeletedAt);

        var purge = await dbContext.PendingPurges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.AthleteId == athlete.Id);
        Assert.NotNull(purge);
        Assert.Equal(PendingPurge.PendingStatus, purge.Status);
    }

    [Fact]
    public async Task Given_MultipleAthletes_When_OneDeleted_Then_OtherAthletesRemainActiveAndUnaffected()
    {
        // 1. Arrange: Create 2 Athletes with PF-A
        var athlete1Response = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Atleta", "Uno", "atleta1@test.com", "+5491100000001", false));
        var athlete2Response = await _clientPfA.PostAsJsonAsync("/api/v1/athletes",
            new CreateAthleteRequest("Atleta", "Dos", "atleta2@test.com", "+5491100000002", false));

        var athlete1 = await athlete1Response.Content.ReadFromJsonAsync<AthleteDto>();
        var athlete2 = await athlete2Response.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete1);
        Assert.NotNull(athlete2);

        // 2. Act: Delete Athlete 1
        var deleteResponse = await _clientPfA.DeleteAsync($"/api/v1/athletes/{athlete1.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // 3. Assert: Athlete 2 is still retrieved normally and listed
        var getAthlete2 = await _clientPfA.GetAsync($"/api/v1/athletes/{athlete2.Id}");
        Assert.Equal(HttpStatusCode.OK, getAthlete2.StatusCode);

        var listResponse = await _clientPfA.GetAsync("/api/v1/athletes");
        var list = await listResponse.Content.ReadFromJsonAsync<List<AthleteDto>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, a => a.Id == athlete1.Id);
        Assert.Contains(list, a => a.Id == athlete2.Id);

        // Database checks
        using var dbContext = _factory.CreateDbContext();
        var dbAthlete2 = await dbContext.Athletes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == athlete2.Id);
        Assert.NotNull(dbAthlete2);
        Assert.Null(dbAthlete2.DeletedAt);

        var athlete2Purges = await dbContext.PendingPurges
            .IgnoreQueryFilters()
            .Where(p => p.AthleteId == athlete2.Id)
            .ToListAsync();
        Assert.Empty(athlete2Purges);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
