using System.Net;
using System.Net.Http.Json;
using Application.DTOs.TrafficLight;
using Domain.Common;
using Domain.Entities;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// Certificación HTTP del semáforo por categoría (KAN-367):
// GET /api/v1/categories/{id}/traffic-light-today debe responder 200 con la clasificación
// correcta aunque Estructura esté apagada, con timeout de resiliencia rechazado o con el
// circuit breaker abierto. El microservicio del Asistente no participa en absoluto: toda
// esta suite corre sin ningún otro servicio encendido (blindada además por TrafficLightIsolationTests).
public sealed class TrafficLightEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    private static string Endpoint(Guid categoryId) =>
        $"api/v1/categories/{categoryId}/traffic-light-today";

    // ---------- a) Estructura apagada: 200 con los check-ins locales ----------

    [Fact]
    public async Task Given_StructureDown_When_GetTrafficLightToday_Then_Returns200WithLocalDegradedCheckIns()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var athleteId = Guid.NewGuid();
        await SeedCheckInAsync(factory, athleteId, TrafficLight.Green, "Estado óptimo");

        // Por defecto el handler falso tiene Estructura apagada (conexión rechazada).
        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AthleteTrafficLightSummaryDto>>();
        var summary = Assert.Single(summaries!);

        // Modo degradado: los check-ins locales se devuelven aunque no tengamos los nombres.
        Assert.Equal(athleteId, summary.AthleteId);
        Assert.Equal(string.Empty, summary.AthleteName);
        Assert.True(summary.HasReportedToday);
        Assert.Equal("Green", summary.TrafficLight);
        Assert.Equal(90, summary.ResultValue);
    }

    [Fact]
    public async Task Given_StructureDownAndNoCheckIns_When_GetTrafficLightToday_Then_Returns200WithEmptyList()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AthleteTrafficLightSummaryDto>>();
        Assert.NotNull(summaries);
        Assert.Empty(summaries);
    }

    // ---------- b) Timeout de resiliencia rechazado: 200 degradado ----------

    [Fact]
    public async Task Given_ResilienceTimeoutRejected_When_GetTrafficLightToday_Then_Returns200NotServerError()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.SimulateTimeoutRejection();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AthleteTrafficLightSummaryDto>>();
        Assert.NotNull(summaries);
        Assert.Empty(summaries);
    }

    // ---------- c) Circuit breaker abierto: 200, nunca 500 (regresión KAN-367) ----------

    [Fact]
    public async Task Given_OpenCircuitBreaker_When_GetTrafficLightToday_Then_Returns200Not500()
    {
        // Antes del fix, BrokenCircuitException escapaba de StructureClient y el middleware
        // respondía 500: el PF se quedaba sin pantalla aunque hubiera datos locales.
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.SimulateOpenCircuit();

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AthleteTrafficLightSummaryDto>>();
        Assert.NotNull(summaries);
        Assert.Empty(summaries);
    }

    // ---------- d) Estructura viva: clasificación exacta y orden por severidad ----------

    [Fact]
    public async Task Given_StructureAlive_When_GetTrafficLightToday_Then_ClassifiesEachAthleteBySeverity()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var redId = Guid.NewGuid();
        var yellowId = Guid.NewGuid();
        var greenId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();

        await SeedCheckInAsync(factory, redId, TrafficLight.Red, "Lesión activa", hasInjury: true);
        await SeedCheckInAsync(factory, yellowId, TrafficLight.Yellow, "Señales de alerta: poco sueño");
        await SeedCheckInAsync(factory, greenId, TrafficLight.Green, "Estado óptimo");

        factory.Structure.RespondWithAthletes($$"""
            [
              {"id":"{{redId}}","fullName":"Ana Roja"},
              {"id":"{{yellowId}}","fullName":"Bruno Alerta"},
              {"id":"{{greenId}}","fullName":"Carla Optima"},
              {"id":"{{pendingId}}","fullName":"Eze Pendiente"}
            ]
            """);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AthleteTrafficLightSummaryDto>>();
        Assert.NotNull(summaries);

        // Severidad Rojo → Amarillo (reportó y pendiente, orden estable) → Verde.
        Assert.Equal(
            new[] { redId, yellowId, pendingId, greenId },
            summaries.Select(item => item.AthleteId).ToArray());

        var red = summaries[0];
        Assert.Equal("Ana Roja", red.AthleteName);
        Assert.Equal("Red", red.TrafficLight);
        Assert.Equal("Lesión activa", red.TrafficLightReason);
        Assert.Equal(20, red.ResultValue);
        Assert.True(red.HasReportedToday);
        Assert.NotNull(red.Indicators);

        var yellow = summaries[1];
        Assert.Equal("Bruno Alerta", yellow.AthleteName);
        Assert.Equal("Yellow", yellow.TrafficLight);
        Assert.Equal("Señales de alerta: poco sueño", yellow.TrafficLightReason);
        Assert.Equal(50, yellow.ResultValue);

        var pending = summaries[2];
        Assert.Equal("Eze Pendiente", pending.AthleteName);
        Assert.Equal("Yellow", pending.TrafficLight);
        Assert.Contains("sin check-in", pending.TrafficLightReason, StringComparison.Ordinal);
        Assert.Equal(50, pending.ResultValue);
        Assert.False(pending.HasReportedToday);
        Assert.Null(pending.ReportedAt);
        Assert.Null(pending.Indicators);

        var green = summaries[3];
        Assert.Equal("Carla Optima", green.AthleteName);
        Assert.Equal("Green", green.TrafficLight);
        Assert.Equal("Estado óptimo", green.TrafficLightReason);
        Assert.Equal(90, green.ResultValue);
        Assert.True(green.HasReportedToday);
    }

    // ---------- Autorización del endpoint (contrato: 401/403/404) ----------

    [Fact]
    public async Task Given_NoToken_When_GetTrafficLightToday_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateClient().GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_GetTrafficLightToday_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_UnknownCategoryInStructure_When_GetTrafficLightToday_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.NotFound, """{"title":"Categoría no encontrada."}""");

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Siembra el check-in de hoy (hora Argentina) tal como lo persistiría el caso de uso:
    // el color y el motivo se guardan en la fila y el endpoint los lee de ahí, sin re-evaluar.
    private static Task SeedCheckInAsync(
        SeguimientoTestApplicationFactory factory,
        Guid athleteId,
        TrafficLight trafficLight,
        string reason,
        bool hasInjury = false) =>
        factory.ExecuteInDbContextAsync(db =>
        {
            db.CheckIns.Add(new CheckIn
            {
                AthleteId = athleteId,
                Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue),
                TrafficLight = trafficLight,
                TrafficLightReason = reason,
                SleepHours = 8,
                SleepQuality = 4,
                MuscleSoreness = 1,
                FatigueLevel = 2,
                MoodRate = 4,
                StressLevel = 2,
                IsSick = false,
                EffortRate = 4,
                HasActiveInjury = hasInjury,
            });
            return db.SaveChangesAsync();
        });
}
