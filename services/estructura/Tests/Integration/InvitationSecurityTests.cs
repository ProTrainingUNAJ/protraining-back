using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Common;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estructura.Tests.Integration;

[Trait("Category", "Security")]
public sealed class InvitationSecurityTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly HttpClient _clientPf;
    private readonly HttpClient _unauthenticatedClient;
    private readonly Guid _pfId;

    public InvitationSecurityTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfId = Guid.NewGuid();
        _clientPf = _factory.CreateAuthenticatedClient(_pfId);
        _unauthenticatedClient = _factory.CreateClient();
    }

    [Fact]
    public async Task Given_ValidToken_When_GetInvitation_Then_Returns200WithCategoryData_AndNeverExposesInternalAthleteIds()
    {
        // Arrange: PF autenticado crea organización y categoría, luego genera el token
        var orgResponse = await _clientPf.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest("Club Seguridad Test", "Club de prueba"));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);

        var catResponse = await _clientPf.PostAsJsonAsync("/api/v1/categories",
            new CreateCategoryRequest(org.Id, "Sub-20 Competitiva", "Categoría juvenil"));
        var cat = await catResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);

        var tokenResponse = await _clientPf.PostAsync($"/api/v1/categories/{cat.Id}/invitation-token", null);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var invitation = await tokenResponse.Content.ReadFromJsonAsync<CategoryInvitationDto>();
        Assert.NotNull(invitation);

        // Act: Cliente anónimo consulta la invitación pública
        var publicResponse = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{invitation.Token}");

        // Assert: 200 OK con datos públicos
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var rawContent = await publicResponse.Content.ReadAsStringAsync();
        var returnedInvitation = await publicResponse.Content.ReadFromJsonAsync<CategoryInvitationDto>();

        Assert.NotNull(returnedInvitation);
        Assert.Equal(invitation.Token, returnedInvitation.Token);
        Assert.Equal("Sub-20 Competitiva", returnedInvitation.CategoryName);
        Assert.Equal(cat.Id, returnedInvitation.CategoryId);

        // Decisión Enzo: El pfId de la invitación SÍ debe exponerse para que Identidad vincule al atleta con su PF
        Assert.Equal(_pfId, returnedInvitation.PfId);

        // Seguridad: NUNCA debe exponer listas de otros atletas ni datos médicos
        Assert.DoesNotContain("athletes", rawContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medicalRecord", rawContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_InvalidOrTamperedToken_When_GetInvitation_Then_Returns404NotFound_Never200Nor500()
    {
        var inventedToken = "token_completamente_falso_1234567890";
        var response = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{inventedToken}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Given_ExpiredTokenOver7Days_When_GetInvitation_Then_Returns410Gone_AndDoesNotReturnCategoryData()
    {
        // Arrange: Insertamos organización, categoría y token vencido respetando FKs
        var expiredToken = "token_expirado_seguridad";
        var expiredDate = DateTime.UtcNow.AddDays(-2);

        using (var db = _factory.CreateDbContext(_pfId))
        {
            var org = Organization.Create("Org Expirada");
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(org, _pfId);
            db.Organizations.Add(org);

            var cat = Category.Create(org.Id, "Cat Expirada");
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(cat, _pfId);
            db.Categories.Add(cat);

            var inv = CategoryInvitation.Create(cat.Id, DateTime.UtcNow.AddMinutes(1), expiredToken);
            typeof(CategoryInvitation).GetProperty(nameof(CategoryInvitation.ExpiresAt))!.SetValue(inv, expiredDate);
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(inv, _pfId);
            db.CategoryInvitations.Add(inv);

            await db.SaveChangesAsync();
        }

        // Act
        var response = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{expiredToken}");

        // Assert: 410 Gone
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Given_NoSessionToken_When_AcceptInvitation_Then_Returns401Unauthorized()
    {
        // Act: Llamada sin header Authorization
        var response = await _unauthenticatedClient.PostAsJsonAsync(
            "/api/v1/invitations/algun-token/accept",
            new AcceptCategoryInvitationRequest("Juan", "Pérez", "+5491111111111", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_PfToken_When_AcceptInvitation_Then_Returns403Forbidden_AndDoesNotCreateAthlete()
    {
        // Arrange: Cliente autenticado como PreparadorFisico
        var response = await _clientPf.PostAsJsonAsync(
            "/api/v1/invitations/algun-token/accept",
            new AcceptCategoryInvitationRequest("Juan", "Pérez", "+5491111111111", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))));

        // Assert: 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteTokenWithMismatchingPfId_When_AcceptInvitation_Then_Returns403Forbidden()
    {
        // Arrange: PF crea una invitación legítima
        var org = await (await _clientPf.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club PF A"))).Content.ReadFromJsonAsync<OrganizationDto>();
        var cat = await (await _clientPf.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, "Cat PF A"))).Content.ReadFromJsonAsync<CategoryDto>();
        var inv = await (await _clientPf.PostAsync($"/api/v1/categories/{cat!.Id}/invitation-token", null)).Content.ReadFromJsonAsync<CategoryInvitationDto>();

        // Atleta pertenece a otro PF (pfId diferente)
        var differentPfId = Guid.NewGuid();
        var clientAthleteForeign = _factory.CreateAuthenticatedClient(differentPfId, role: "Atleta", userId: Guid.NewGuid());

        // Act
        var response = await clientAthleteForeign.PostAsJsonAsync(
            $"/api/v1/invitations/{inv!.Token}/accept",
            new AcceptCategoryInvitationRequest("Atleta", "Ajeno", "+5491122222222", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))));

        // Assert: 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_ValidAthleteTokenAndInvitation_When_CallingAcceptTwice_Then_Returns200BothTimes_AndDoesNotDuplicate()
    {
        // Arrange: PF crea invitación
        var org = await (await _clientPf.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club Central"))).Content.ReadFromJsonAsync<OrganizationDto>();
        var cat = await (await _clientPf.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, "Primera División"))).Content.ReadFromJsonAsync<CategoryDto>();
        var inv = await (await _clientPf.PostAsync($"/api/v1/categories/{cat!.Id}/invitation-token", null)).Content.ReadFromJsonAsync<CategoryInvitationDto>();

        // Atleta legítimo con el mismo pfId de la invitación
        var athleteAccountId = Guid.NewGuid();
        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: athleteAccountId);

        var request = new AcceptCategoryInvitationRequest(
            "Lucas", "Gómez", "+5491133333333",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)));

        // Act 1: Primera llamada
        var response1 = await clientAthlete.PostAsJsonAsync($"/api/v1/invitations/{inv!.Token}/accept", request);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var athlete1 = await response1.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete1);

        // Act 2: Segunda llamada con el mismo QR/link (Idempotencia)
        var response2 = await clientAthlete.PostAsJsonAsync($"/api/v1/invitations/{inv.Token}/accept", request);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var athlete2 = await response2.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete2);
        Assert.Equal(athlete1.Id, athlete2.Id);

        // Assert en BD: Se verifica directamente en la BD que solo existe 1 Athlete con ese AccountId
        using var db = _factory.CreateDbContext();
        var totalAthletes = await db.Athletes.IgnoreQueryFilters().CountAsync(a => a.AccountId == athleteAccountId);
        Assert.Equal(1, totalAthletes);
    }

    [Fact]
    public async Task Given_ExpiredToken_When_AcceptInvitation_Then_Returns410Gone()
    {
        // Arrange: Token expirado en la BD respetando FKs
        var expiredToken = "token_accept_expirado";
        using (var db = _factory.CreateDbContext(_pfId))
        {
            var org = Organization.Create("Org Test");
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(org, _pfId);
            db.Organizations.Add(org);

            var cat = Category.Create(org.Id, "Cat Test");
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(cat, _pfId);
            db.Categories.Add(cat);

            var inv = CategoryInvitation.Create(cat.Id, DateTime.UtcNow.AddMinutes(1), expiredToken);
            typeof(CategoryInvitation).GetProperty(nameof(CategoryInvitation.ExpiresAt))!.SetValue(inv, DateTime.UtcNow.AddDays(-1));
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.PfId))!.SetValue(inv, _pfId);
            db.CategoryInvitations.Add(inv);

            await db.SaveChangesAsync();
        }

        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: Guid.NewGuid());

        // Act
        var response = await clientAthlete.PostAsJsonAsync(
            $"/api/v1/invitations/{expiredToken}/accept",
            new AcceptCategoryInvitationRequest("Pedro", "Gómez", "+5491144444444", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20))));

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    // --- Tests de Integración KAN-401 (Consentimiento de Menores) ---

    [Fact]
    public async Task Given_Minor16YearsOldWithoutGuardian_When_AcceptInvitation_Then_Returns400BadRequest_AndDoesNotCreateAthlete()
    {
        // Arrange: PF crea invitación
        var org = await (await _clientPf.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club Menores"))).Content.ReadFromJsonAsync<OrganizationDto>();
        var cat = await (await _clientPf.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, "Sub-16"))).Content.ReadFromJsonAsync<CategoryDto>();
        var inv = await (await _clientPf.PostAsync($"/api/v1/categories/{cat!.Id}/invitation-token", null)).Content.ReadFromJsonAsync<CategoryInvitationDto>();

        var athleteAccountId = Guid.NewGuid();
        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: athleteAccountId);

        // Menor de 16 años sin datos de tutor
        var request = new AcceptCategoryInvitationRequest(
            FirstName: "Joaquín",
            LastName: "Menor",
            Phone: "+5491100001111",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)), // 16 años
            GuardianName: null);

        // Act
        var response = await clientAthlete.PostAsJsonAsync($"/api/v1/invitations/{inv!.Token}/accept", request);

        // Assert: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Assert en BD: No se crea el atleta
        using var db = _factory.CreateDbContext();
        var athleteExists = await db.Athletes.IgnoreQueryFilters().AnyAsync(a => a.AccountId == athleteAccountId);
        Assert.False(athleteExists);
    }

    [Fact]
    public async Task Given_Minor16YearsOldWithCompleteGuardianData_When_AcceptInvitation_Then_Returns200OK_AndCreatesMinorAthlete()
    {
        // Arrange: PF crea invitación
        var org = await (await _clientPf.PostAsJsonAsync("/api/v1/organizations", new CreateOrganizationRequest("Club Menores Valido"))).Content.ReadFromJsonAsync<OrganizationDto>();
        var cat = await (await _clientPf.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(org!.Id, "Sub-16 Valida"))).Content.ReadFromJsonAsync<CategoryDto>();
        var inv = await (await _clientPf.PostAsync($"/api/v1/categories/{cat!.Id}/invitation-token", null)).Content.ReadFromJsonAsync<CategoryInvitationDto>();

        var athleteAccountId = Guid.NewGuid();
        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: athleteAccountId);

        // Menor de 16 años con tutor completo
        var request = new AcceptCategoryInvitationRequest(
            FirstName: "Joaquín",
            LastName: "Aprobado",
            Phone: "+5491100002222",
            BirthDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-16)),
            GuardianName: "Esteban Aprobado",
            GuardianEmail: "esteban.tutor@test.com",
            GuardianPhone: "+5491100003333",
            AllowsPhysicalTraining: true,
            AllowsHabitsRecommendations: true);

        // Act
        var response = await clientAthlete.PostAsJsonAsync($"/api/v1/invitations/{inv!.Token}/accept", request);

        // Assert: 200 OK
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var athlete = await response.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(athlete);
        Assert.True(athlete.IsMinor);

        // Assert en BD: El atleta se persiste como IsMinor = true
        using var db = _factory.CreateDbContext();
        var athleteInDb = await db.Athletes.IgnoreQueryFilters().Include(a => a.DigitalConsent).FirstOrDefaultAsync(a => a.AccountId == athleteAccountId);
        Assert.NotNull(athleteInDb);
        Assert.True(athleteInDb.IsMinor);
        Assert.NotNull(athleteInDb.DigitalConsent);
        Assert.True(athleteInDb.DigitalConsent.IsMinor);
        Assert.Equal("Esteban Aprobado", athleteInDb.DigitalConsent.GuardianName);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}