using System.Net;
using System.Net.Http.Json;
using Application.DTOs.CheckIns;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// KAN-450: un atleta no ve ni toca datos de un compañero. Por HTTP, de punta a punta.
[Trait("Category", "Isolation")]
public sealed class AthletePrivacyEndpointTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _myAthleteId = Guid.NewGuid();
    private readonly Guid _teammateId = Guid.NewGuid();

    private static SubmitCheckInRequest RequestValida() => new()
    {
        SleepHours = 8,
        SleepQuality = 4,
        MuscleSoreness = 1,
        FatigueLevel = 2,
        MoodRate = 4,
        StressLevel = 2,
        EffortRate = 4
    };

    [Fact]
    public async Task Given_Athlete_When_SubmitsCheckInWithTeammateId_Then_Returns404AndPersistsNothing()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        // Estructura dice quién es el atleta del token (/me): soy yo, no mi compañero.
        factory.Structure.RespondWith(HttpStatusCode.OK, $$"""{"athleteId":"{{_myAthleteId}}","fullName":"Ana Pérez","categories":[]}""");

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsJsonAsync($"api/v1/athletes/{_teammateId}/check-in", RequestValida());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var checkIns = await factory.ExecuteInDbContextAsync(db => db.CheckIns.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, checkIns);
    }

    [Fact]
    public async Task Given_Athlete_When_SubmitsCheckInWithOwnId_Then_Returns201()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.OK, $$"""{"athleteId":"{{_myAthleteId}}","fullName":"Ana Pérez","categories":[]}""");

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .PostAsJsonAsync($"api/v1/athletes/{_myAthleteId}/check-in", RequestValida());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Given_Athlete_When_ListsTeammateInjuries_Then_Returns403ByRoleWithoutAskingStructure()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .GetAsync($"api/v1/athletes/{_teammateId}/injuries");

        // Las lesiones son solo para el PF: el atleta recibe 403 por su rol, igual para cualquier id,
        // así que no revela si el compañero existe.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(factory.Structure.LastRequest);
    }
}
