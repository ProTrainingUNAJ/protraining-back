using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Athletes;
using Domain.Entities;
using Estructura.Tests.Infrastructure;

namespace Estructura.Tests.Integration;

public sealed class AthleteTelegramEndpointsTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();
    private readonly HttpClient _clientPfA;
    private readonly HttpClient _clientPfB;

    public AthleteTelegramEndpointsTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId, role: "PreparadorFisico");
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId, role: "PreparadorFisico");
    }

    [Fact]
    public async Task Given_ValidTelegram_When_CreateAndUpdateAthlete_Then_NormalizesPersistsAndClears()
    {
        var createRequest = new CreateAthleteRequest(
            FirstName: "Lucas",
            LastName: "Fernández",
            Email: $"lucas.{Guid.NewGuid():N}@club.com",
            Phone: "+54 11 5555-1234",
            IsMinor: false,
            GuardianName: null,
            GuardianEmail: null,
            GuardianPhone: null,
            AllowsPhysicalTraining: true,
            AllowsHabitsRecommendations: true,
            EmergencyContact: null,
            TelegramUsername: "@lucas_fernandez");

        var createResponse = await _clientPfA.PostAsJsonAsync("/api/v1/athletes", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(created);
        Assert.Equal("lucas_fernandez", created.TelegramUsername);

        var invalidUpdate = await _clientPfA.PutAsJsonAsync(
            $"/api/v1/athletes/{created.Id}",
            new UpdateAthleteRequest(Phone: "+54 11 5555-1234", EmergencyContact: null, TelegramUsername: "ab"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);

        var clearUpdate = await _clientPfA.PutAsJsonAsync(
            $"/api/v1/athletes/{created.Id}",
            new UpdateAthleteRequest(Phone: "+54 11 5555-1234", EmergencyContact: null, TelegramUsername: ""));
        Assert.Equal(HttpStatusCode.OK, clearUpdate.StatusCode);

        var cleared = await clearUpdate.Content.ReadFromJsonAsync<AthleteDto>();
        Assert.NotNull(cleared);
        Assert.Null(cleared.TelegramUsername);
    }

    [Fact]
    public async Task Given_AthleteBelongingToPfA_When_PfBTriesToUpdateTelegram_Then_Returns404()
    {
        var athlete = Athlete.CreateAdult(
            "Enzo",
            "Fernández",
            $"enzo.{Guid.NewGuid():N}@club.com",
            "+5491100004444",
            signsInitialConsent: true,
            telegramUsername: "enzo_ok");

        using (var dbContext = _factory.CreateDbContext(_pfAId))
        {
            dbContext.Athletes.Add(athlete);
            await dbContext.SaveChangesAsync();
        }

        var response = await _clientPfB.PutAsJsonAsync(
            $"/api/v1/athletes/{athlete.Id}",
            new UpdateAthleteRequest(Phone: null, EmergencyContact: null, TelegramUsername: "hacked_user"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public ValueTask DisposeAsync()
    {
        _clientPfA.Dispose();
        _clientPfB.Dispose();
        return _factory.DisposeAsync();
    }
}
