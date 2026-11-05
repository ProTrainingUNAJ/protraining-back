using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Application.DTOs.Categories;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Estructura.Tests.Integration;

public sealed class AthleteConsentEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfId;
    private readonly HttpClient _clientPf;

    public AthleteConsentEndpointsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfId = Guid.NewGuid();
        _clientPf = _factory.CreateAuthenticatedClient(_pfId, role: "PreparadorFisico");
    }

    [Fact]
    public async Task Given_AuthenticatedAdultAthleteWithoutConsent_When_RegisterConsent_Then_Returns200AndSavesConsent()
    {
        // arrange: crear atleta adulto sin consentimiento inicial
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult(
            "Marcos",
            "Rojo",
            "marcos.rojo@club.com",
            "+5491100001111",
            signsInitialConsent: false);
        athlete.LinkAccount(accountId);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: accountId);

        // verificar que antes de firmar, GET /api/v1/me/consent reporta no firmado
        var initialCheck = await clientAthlete.GetAsync("/api/v1/me/consent");
        var initialStatus = await initialCheck.Content.ReadFromJsonAsync<ConsentStatusDto>();
        Assert.NotNull(initialStatus);
        Assert.False(initialStatus.HasSignedConsent);
        Assert.False(initialStatus.IsMinor);

        // act: registrar consentimiento digital
        var response = await clientAthlete.PostAsJsonAsync(
            "/api/v1/me/consent",
            new RegisterAdultConsentRequest(AllowsHabitsRecommendations: true, AllowsPhysicalTraining: true));

        // assert: HTTP 200 OK y refleja firma
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var consentResult = await response.Content.ReadFromJsonAsync<ConsentStatusDto>();
        Assert.NotNull(consentResult);
        Assert.True(consentResult.HasSignedConsent);
        Assert.False(consentResult.IsMinor);
        Assert.True(consentResult.AllowsPhysicalTraining);
        Assert.True(consentResult.AllowsHabitsRecommendations);
        Assert.NotNull(consentResult.ConsentDate);

        // verificar persistencia en base de datos
        using (var verifyDb = _factory.CreateDbContext())
        {
            var dbConsent = await verifyDb.DigitalConsents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.AthleteId == athlete.Id);

            Assert.NotNull(dbConsent);
            Assert.True(dbConsent.IsSigned);
            Assert.False(dbConsent.IsMinor);
            Assert.True(dbConsent.AllowsPhysicalTraining);
            Assert.True(dbConsent.AllowsHabitsRecommendations);
        }

        // verificar que GET /api/v1/me y GET /api/v1/me/consent ahora devuelven firmado
        var meResponse = await clientAthlete.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var meDto = await meResponse.Content.ReadFromJsonAsync<MeDto>();
        Assert.NotNull(meDto);
        Assert.True(meDto.HasSignedConsent);
    }

    [Fact]
    public async Task Given_AuthenticatedMinorAthlete_When_AttemptRegisterConsent_Then_Returns400BadRequestWithClearMessage()
    {
        // arrange: crear atleta menor de edad
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateMinor(
            "Lautaro",
            "Perez",
            "lautaro.perez@club.com",
            "+5491122223333",
            guardianName: "Carlos Perez",
            guardianEmail: "carlos.perez@mail.com",
            guardianPhone: "+5491144445555",
            allowsPhysicalTraining: true,
            allowsHabitsRecommendations: false);
        athlete.LinkAccount(accountId);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: accountId);

        // act: intentar registrar consentimiento como menor
        var response = await clientAthlete.PostAsJsonAsync(
            "/api/v1/me/consent",
            new RegisterAdultConsentRequest(AllowsHabitsRecommendations: true, AllowsPhysicalTraining: true));

        // assert: HTTP 400 Bad Request indicando que requiere tutor legal
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("tutor", problem.Title ?? problem.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_AdultAthleteWhoAlreadySigned_When_RegisterConsentAgain_Then_Returns400BadRequest()
    {
        // arrange: crear atleta adulto que ya firmó
        var accountId = Guid.NewGuid();
        var athlete = Athlete.CreateAdult(
            "Esteban",
            "Andrada",
            "esteban.a@club.com",
            "+5491133334444",
            signsInitialConsent: true);
        athlete.LinkAccount(accountId);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: accountId);

        // act: intentar firmar de nuevo
        var response = await clientAthlete.PostAsJsonAsync(
            "/api/v1/me/consent",
            new RegisterAdultConsentRequest(AllowsHabitsRecommendations: false, AllowsPhysicalTraining: true));

        // assert: HTTP 400 Bad Request por registro inmutable
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("inmutable", problem.Title ?? problem.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_UnauthenticatedUser_When_AccessConsentEndpoints_Then_Returns401Unauthorized()
    {
        var anonymousClient = _factory.CreateClient();

        var getResponse = await anonymousClient.GetAsync("/api/v1/me/consent");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var postResponse = await anonymousClient.PostAsJsonAsync(
            "/api/v1/me/consent",
            new RegisterAdultConsentRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    [Fact]
    public async Task Given_PfRole_When_AccessConsentEndpoints_Then_Returns403Forbidden()
    {
        var getResponse = await _clientPf.GetAsync("/api/v1/me/consent");
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        var postResponse = await _clientPf.PostAsJsonAsync(
            "/api/v1/me/consent",
            new RegisterAdultConsentRequest());
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteAcceptedInvitation_When_ChecksConsentAndSigns_Then_ConsentIsPersistedAndHasSignedConsentIsTrue()
    {
        // 1. Arrange: Crear organización y categoría en base de datos
        var organization = Organization.Create("Club Atlético Test", "Descripción");
        var category = Category.Create(organization.Id, "Sub-20");
        var token = "token-real-" + Guid.NewGuid().ToString("N")[..12];
        var invitation = CategoryInvitation.Create(category.Id, DateTime.UtcNow.AddDays(7), token);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Organizations.Add(organization);
            dbContext.Categories.Add(category);
            dbContext.CategoryInvitations.Add(invitation);
            await dbContext.SaveChangesAsync();
        }

        // 2. Atleta autenticado con su accountId y el mismo pfId de la invitación
        var athleteAccountId = Guid.NewGuid();
        var clientAthlete = _factory.CreateAuthenticatedClient(_pfId, role: "Atleta", userId: athleteAccountId);

        // 3. Act 1: Aceptar la invitación
        var acceptResponse = await clientAthlete.PostAsJsonAsync(
            $"/api/v1/invitations/{token}/accept",
            new AcceptCategoryInvitationRequest(
                FirstName: "Franco",
                LastName: "Armani",
                Email: "franco.armani@test.com",
                Phone: "+5491122334455"));

        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        // 4. Act 2: Consultar estado de consentimiento inicial (debe ser false)
        var consentCheck = await clientAthlete.GetAsync("/api/v1/me/consent");
        Assert.Equal(HttpStatusCode.OK, consentCheck.StatusCode);
        var initialStatus = await consentCheck.Content.ReadFromJsonAsync<ConsentStatusDto>();
        Assert.NotNull(initialStatus);
        Assert.False(initialStatus.HasSignedConsent, "El atleta creado por invitación debe iniciar sin consentimiento firmado.");
        Assert.False(initialStatus.IsMinor);

        var meCheck = await clientAthlete.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, meCheck.StatusCode);
        var meDto = await meCheck.Content.ReadFromJsonAsync<MeDto>();
        Assert.NotNull(meDto);
        Assert.False(meDto.HasSignedConsent);

        // 5. Act 3: El atleta firma el consentimiento con POST /api/v1/me/consent
        var signResponse = await clientAthlete.PostAsJsonAsync(
            "/api/v1/me/consent",
            new RegisterAdultConsentRequest(AllowsHabitsRecommendations: true, AllowsPhysicalTraining: true));

        Assert.Equal(HttpStatusCode.OK, signResponse.StatusCode);
        var signedStatus = await signResponse.Content.ReadFromJsonAsync<ConsentStatusDto>();
        Assert.NotNull(signedStatus);
        Assert.True(signedStatus.HasSignedConsent);
        Assert.True(signedStatus.AllowsHabitsRecommendations);
        Assert.True(signedStatus.AllowsPhysicalTraining);

        // 6. Assert: Verificar en base de datos
        using (var verifyDb = _factory.CreateDbContext(_pfId))
        {
            var athlete = await verifyDb.Athletes
                .Include(a => a.DigitalConsent)
                .FirstOrDefaultAsync(a => a.AccountId == athleteAccountId);

            Assert.NotNull(athlete);
            Assert.True(athlete.HasSignedConsent);
            Assert.NotNull(athlete.DigitalConsent);
            Assert.True(athlete.DigitalConsent.IsSigned);
            Assert.True(athlete.DigitalConsent.AllowsHabitsRecommendations);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
