using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Evolution;
using Domain.Entities;
using Seguimiento.Tests.Infrastructure;
using Xunit;

namespace Seguimiento.Tests.Integration;

public sealed class AthleteEvolutionEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _athleteId = Guid.NewGuid();

    private string Endpoint(Guid athleteId) =>
        $"api/v1/athletes/{athleteId}/evolution";

    [Fact]
    public async Task GetEvolution_SinToken_Devuelve401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateClient().GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEvolution_RolAtleta_MismoAtleta_Devuelve200()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.OK, $"{{\"athleteId\":\"{_athleteId}\"}}");
        factory.Training.RespondWith(HttpStatusCode.OK, "[]");

        var client = factory.CreateAuthenticatedClient(_pfId, role: "Atleta");
        var response = await client.GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetEvolution_RolAtleta_AtletaAjeno_Devuelve404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.OK, $"{{\"athleteId\":\"{Guid.NewGuid()}\"}}");

        var client = factory.CreateAuthenticatedClient(_pfId, role: "Atleta");
        var response = await client.GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEvolution_RolPf_Devuelve200ConCurvas()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();

        // 1. Sembrar check-in local
        var nowUtc = DateTime.UtcNow;
        await factory.ExecuteInDbContextAsync(async context =>
        {
            context.CheckIns.Add(new CheckIn
            {
                AthleteId = _athleteId,
                Date = nowUtc,
                FatigueLevel = 3,
                SleepHours = 7.5,
                SleepQuality = 4,
                MuscleSoreness = 2,
                MoodRate = 4,
                StressLevel = 2,
                TrafficLight = TrafficLight.Green,
                HasActiveInjury = false
            });

            var catalogTest = context.CatalogTests.First();
            context.FieldTestResults.Add(new FieldTestResult
            {
                SessionId = Guid.NewGuid(),
                AthleteId = _athleteId,
                CatalogTestId = catalogTest.Id,
                Value = 44.5m,
                Attempt = 1,
                RecordedAt = nowUtc
            });

            await context.SaveChangesAsync();
        });

        // 2. Configurar respuesta simulada de Entrenamiento
        var exerciseId = Guid.NewGuid();
        var recordsJson = $$"""
        [
            {
                "id": "{{Guid.NewGuid()}}",
                "athleteId": "{{_athleteId}}",
                "exerciseId": "{{exerciseId}}",
                "exerciseName": "Sentadilla Trasera",
                "weightOneRepMaxKg": 135.0,
                "origin": "Tested",
                "achievedAt": "{{nowUtc:O}}",
                "isCurrent": true
            }
        ]
        """;
        factory.Training.RespondWith(HttpStatusCode.OK, recordsJson);

        // 3. Consultar como PF
        var client = factory.CreateAuthenticatedClient(_pfId, role: "PreparadorFisico");
        var response = await client.GetAsync(Endpoint(_athleteId));

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"TEST_FAIL: {response.StatusCode} - {body}");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evolution = await response.Content.ReadFromJsonAsync<AthleteEvolutionResponseDto>();
        Assert.NotNull(evolution);
        Assert.Equal(_athleteId, evolution.AthleteId);
        Assert.False(evolution.StrengthUnavailable);

        // Curvas de fuerza
        Assert.Single(evolution.Strength);
        Assert.Equal("Sentadilla Trasera", evolution.Strength[0].ExerciseName);
        Assert.Single(evolution.Strength[0].Points);
        Assert.Equal(135.0, evolution.Strength[0].Points[0].Value);

        // Curvas de tests físicos
        Assert.Single(evolution.FitnessTests);
        Assert.Single(evolution.FitnessTests[0].Points);
        Assert.Equal(44.5, evolution.FitnessTests[0].Points[0].Value);

        // Curvas de wellness / fatiga
        Assert.Single(evolution.Wellness.FatiguePoints);
        Assert.Equal(3, evolution.Wellness.FatiguePoints[0].Value);
        Assert.Single(evolution.Wellness.DailyPoints);
        Assert.Equal(3, evolution.Wellness.DailyPoints[0].FatigueLevel);
    }

    [Fact]
    public async Task GetEvolution_RolPf_AtletaDeOtroPf_Devuelve404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        // Estructura responde 404 (el atleta no existe o no es de este PF)
        factory.Structure.RespondWith(HttpStatusCode.NotFound, "");

        var client = factory.CreateAuthenticatedClient(_pfId, role: "PreparadorFisico");
        var response = await client.GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEvolution_RolPf_EntrenamientoCaido_Devuelve200ConStrengthUnavailableTrue()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondAllAthletesAreOwn();
        factory.Training.SimulateDown();

        var client = factory.CreateAuthenticatedClient(_pfId, role: "PreparadorFisico");
        var response = await client.GetAsync(Endpoint(_athleteId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evolution = await response.Content.ReadFromJsonAsync<AthleteEvolutionResponseDto>();
        Assert.NotNull(evolution);
        Assert.True(evolution.StrengthUnavailable);
        Assert.Empty(evolution.Strength);
    }
}
