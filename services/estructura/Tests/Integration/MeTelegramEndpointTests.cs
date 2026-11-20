using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Athletes;
using Application.UseCases.Athletes.UpdateMyTelegram;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Estructura.Tests.Integration;

public sealed class MeTelegramEndpointTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly EstructuraTestApplicationFactory _factory = new();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Athlete_CanUpdateOwnTelegram_And_PfSeesUpdatedUsername_WithTenantIsolation()
    {
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var athleteAccountId = Guid.NewGuid();

        Guid seededAthleteId;
        using (var db = _factory.CreateDbContext(pfA))
        {
            var athlete = Athlete.CreateAdult(
                firstName: "Lucas",
                lastName: "Méndez",
                email: $"lucas-{Guid.NewGuid():N}@test.com",
                phone: "+54 9 11 5555-1234",
                emergencyContact: null);
            athlete.LinkAccount(athleteAccountId);
            db.Athletes.Add(athlete);
            await db.SaveChangesAsync();
            seededAthleteId = athlete.Id;
        }

        using var pfAClient = _factory.CreateAuthenticatedClient(pfA, "PreparadorFisico");
        using var pfBClient = _factory.CreateAuthenticatedClient(pfB, "PreparadorFisico");
        using var athleteClient = _factory.CreateAuthenticatedClient(pfA, "Atleta", athleteAccountId);
        using var otherAthleteClient = _factory.CreateAuthenticatedClient(pfA, "Atleta", Guid.NewGuid());

        // 1. PF calling PUT /api/v1/me/telegram -> 403 Forbidden
        var pfForbidden = await pfAClient.PutAsJsonAsync(
            "/api/v1/me/telegram",
            new UpdateMyTelegramRequestDto("@pf_intento"));
        Assert.Equal(HttpStatusCode.Forbidden, pfForbidden.StatusCode);

        // 2. Unauthenticated -> 401
        using var anon = _factory.CreateClient();
        var unauth = await anon.PutAsJsonAsync(
            "/api/v1/me/telegram",
            new UpdateMyTelegramRequestDto("@anon_tg"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        // 3. Another athlete without matching AccountId -> 404 NotFound (cannot touch seeded athlete)
        var otherNotFound = await otherAthleteClient.PutAsJsonAsync(
            "/api/v1/me/telegram",
            new UpdateMyTelegramRequestDto("@otro_tg"));
        Assert.Equal(HttpStatusCode.NotFound, otherNotFound.StatusCode);

        // 4. Invalid format -> 400 BadRequest
        var badRequest = await athleteClient.PutAsJsonAsync(
            "/api/v1/me/telegram",
            new UpdateMyTelegramRequestDto("ab"));
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);

        // 5. Linked athlete updates own Telegram -> 200 OK normalized
        var okResponse = await athleteClient.PutAsJsonAsync(
            "/api/v1/me/telegram",
            new UpdateMyTelegramRequestDto("@lucas_atleta"));
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        var updated = await okResponse.Content.ReadFromJsonAsync<UpdateMyTelegramResponseDto>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("lucas_atleta", updated.TelegramUsername);

        // 6. PF A sees updated Telegram in GET /api/v1/athletes; PF B does not see PF A's athlete
        var pfAAthletes = await pfAClient.GetFromJsonAsync<List<AthleteDto>>("/api/v1/athletes", JsonOptions);
        Assert.NotNull(pfAAthletes);
        var matched = Assert.Single(pfAAthletes, a => a.Id == seededAthleteId);
        Assert.Equal("lucas_atleta", matched.TelegramUsername);

        var pfBAthletes = await pfBClient.GetFromJsonAsync<List<AthleteDto>>("/api/v1/athletes", JsonOptions);
        Assert.NotNull(pfBAthletes);
        Assert.DoesNotContain(pfBAthletes, a => a.Id == seededAthleteId);
    }
}
