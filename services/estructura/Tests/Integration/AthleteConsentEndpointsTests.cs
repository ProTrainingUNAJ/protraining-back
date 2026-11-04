using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
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

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
