using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Estructura.Tests.Integration;

public sealed class AthleteStatusAndEmailEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfId;
    private readonly HttpClient _clientPf;

    public AthleteStatusAndEmailEndpointsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfId = Guid.NewGuid();
        _clientPf = _factory.CreateAuthenticatedClient(_pfId, role: "PreparadorFisico");
    }

    [Fact]
    public async Task Given_Athlete_When_UpdatingStatus_Then_TogglesActiveCorrectly()
    {
        // arrange
        var athlete = Athlete.CreateAdult(
            "Lautaro",
            "Martínez",
            "lautaro.martinez@inter.com",
            "+5491100002222",
            signsInitialConsent: true);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        // act 1: deshabilitar atleta
        var disableResponse = await _clientPf.PatchAsJsonAsync(
            $"/api/v1/athletes/{athlete.Id}/status",
            new UpdateAthleteStatusRequest(Active: false));

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabledDto = await disableResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(disabledDto);
        Assert.False(disabledDto.Active);

        // verify in db
        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            var dbAthlete = await dbContext.Athletes.FindAsync(athlete.Id);
            Assert.NotNull(dbAthlete);
            Assert.False(dbAthlete.Active);
        }

        // act 2: reactivar atleta
        var enableResponse = await _clientPf.PatchAsJsonAsync(
            $"/api/v1/athletes/{athlete.Id}/status",
            new UpdateAthleteStatusRequest(Active: true));

        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
        var enabledDto = await enableResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(enabledDto);
        Assert.True(enabledDto.Active);
    }

    [Fact]
    public async Task Given_Athlete_When_UpdatingEmail_Then_UpdatesEmailInDatabase()
    {
        // arrange
        var athlete = Athlete.CreateAdult(
            "Julián",
            "Álvarez",
            "julian.alvarez@city.com",
            "+5491100003333",
            signsInitialConsent: true);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        // act: actualizar email
        var newEmail = "julian.alvarez@atleti.com";
        var response = await _clientPf.PatchAsJsonAsync(
            $"/api/v1/athletes/{athlete.Id}/email",
            new UpdateAthleteEmailRequest(Email: newEmail));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(dto);
        Assert.Equal(newEmail, dto.Email);

        // verify in db
        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            var dbAthlete = await dbContext.Athletes.FindAsync(athlete.Id);
            Assert.NotNull(dbAthlete);
            Assert.Equal(newEmail, dbAthlete.Email);
        }
    }

    [Fact]
    public async Task Given_CrossTenantAttempt_When_PfBAttemptsToModifyPfAAthlete_Then_Returns404NotFound()
    {
        // arrange: atleta pertenece a _pfId
        var athlete = Athlete.CreateAdult(
            "Emiliano",
            "Martínez",
            "dibu@astonvilla.com",
            "+5491100004444",
            signsInitialConsent: true);

        using (var dbContext = _factory.CreateDbContext(_pfId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        // PF-B ajeno
        var clientPfB = _factory.CreateAuthenticatedClient(Guid.NewGuid(), role: "PreparadorFisico");

        // act & assert status
        var statusResponse = await clientPfB.PatchAsJsonAsync(
            $"/api/v1/athletes/{athlete.Id}/status",
            new UpdateAthleteStatusRequest(Active: false));
        Assert.Equal(HttpStatusCode.NotFound, statusResponse.StatusCode);

        // act & assert email
        var emailResponse = await clientPfB.PatchAsJsonAsync(
            $"/api/v1/athletes/{athlete.Id}/email",
            new UpdateAthleteEmailRequest(Email: "hacker@test.com"));
        Assert.Equal(HttpStatusCode.NotFound, emailResponse.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
