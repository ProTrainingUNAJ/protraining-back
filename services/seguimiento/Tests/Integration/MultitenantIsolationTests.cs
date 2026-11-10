using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.CheckIns;
using Application.DTOs.FitnessTests;
using Application.DTOs.TrafficLight;
using Domain.Common;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Seguimiento.Tests.Infrastructure;
using Xunit;


namespace Seguimiento.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class MultitenantIsolationTests : IAsyncLifetime
{
    private readonly Guid _pfAId = Guid.NewGuid();
    private readonly Guid _pfBId = Guid.NewGuid();
    private readonly Guid _categoryDeA = Guid.NewGuid();
    private readonly Guid _categoryDeB = Guid.NewGuid();
    private readonly Guid _sharedAthleteId = Guid.NewGuid();

    private SeguimientoTestApplicationFactory _factory = null!;
    private HttpClient _clientPfA = null!;
    private HttpClient _clientPfB = null!;
    private CatalogTest _customTestB = null!;

    public async Task InitializeAsync()
    {
        _factory = await SeguimientoTestApplicationFactory.CreateAsync();
        _clientPfA = _factory.CreateAuthenticatedClient(_pfAId);
        _clientPfB = _factory.CreateAuthenticatedClient(_pfBId);

        // Configurar FakeStructureHandler para responder según la categoría consultada
        _factory.Structure.Behavior = (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath?.Trim('/') ?? string.Empty;

            // GET api/v1/categories/{categoryId}/athletes
            if (path.Contains($"api/v1/categories/{_categoryDeA}/athletes", StringComparison.OrdinalIgnoreCase))
            {
                var body = JsonSerializer.Serialize(new[]
                {
                    new { id = _sharedAthleteId, fullName = "Atleta Compartido" }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                });
            }

            // Categoría de B: si la consulta PF A (o si Estructura aísla por PF), responde 404
            if (path.Contains($"api/v1/categories/{_categoryDeB}", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };

        // Sembrar Check-In para PF B con el atleta compartido
        using (var dbB = _factory.CreateDbContext(overridePfId: _pfBId))
        {
            var today = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue);
            var checkInB = new CheckIn
            {
                AthleteId = _sharedAthleteId,
                Date = today,
                TrafficLight = TrafficLight.Red,
                TrafficLightReason = "Cansancio extremo cargado por PF B",
                SleepHours = 4,
                SleepQuality = 1,
                MuscleSoreness = 5,
                FatigueLevel = 5,
                MoodRate = 1,
                StressLevel = 5,
                IsSick = false
            };
            dbB.CheckIns.Add(checkInB);

            // Sembrar test custom creado por PF B
            _customTestB = new CatalogTest(
                name: $"Test Fuerza Custom PF B {_pfBId:N}",
                motorCapacity: MotorCapacity.Strength,
                measurementUnit: "kg",
                difficultyLevel: 2,
                active: true);

            dbB.CatalogTests.Add(_customTestB);
            await dbB.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetCategoryTrafficLightToday_PfAIntentandoConsultarCategoriaDeB_Retorna404()
    {
        // Act: PF A intenta consultar el semáforo de la categoría de PF B
        var response = await _clientPfA.GetAsync($"/api/v1/categories/{_categoryDeB}/traffic-light-today");

        // Assert: Regla de oro KAN-429: siempre 404 para no filtrar existencia
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCategoryTrafficLightToday_CheckInsDePfBNoAparecenEnSemaforoDePfA_InclusoConMismoAthleteId()
    {
        // Act: PF A consulta el semáforo de su propia categoría (donde está el atleta compartido)
        var response = await _clientPfA.GetAsync($"/api/v1/categories/{_categoryDeA}/traffic-light-today");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AthleteTrafficLightSummaryDto>>();
        Assert.NotNull(summaries);
        Assert.Single(summaries, a => a.AthleteId == _sharedAthleteId);

        var athleteSummary = summaries.Single(a => a.AthleteId == _sharedAthleteId);
        // El check-in de PF B (Rojo) NO debe ser visible para PF A; el atleta aparece sin check-in (Verde / Sin datos)
        Assert.NotEqual("Red", athleteSummary.TrafficLight);
    }

    [Fact]
    public async Task GetFitnessTests_PfAVeCatalogoGlobalPeroNoLosTestsCustomDePfB()
    {
        // Act: PF A consulta el catálogo de tests
        var response = await _clientPfA.GetAsync("/api/v1/fitness-tests");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tests = await response.Content.ReadFromJsonAsync<List<FitnessTestDto>>();
        Assert.NotNull(tests);

        // Debe ver los tests del catálogo global (PfId == Guid.Empty sembrados en CreateAsync)
        Assert.NotEmpty(tests);

        // NO debe ver el test personalizado creado por PF B
        Assert.DoesNotContain(tests, t => t.Id == _customTestB.Id);
        Assert.DoesNotContain(tests, t => t.Name == _customTestB.Name);
    }

    [Fact]
    public async Task PfB_ConsultandoFitnessTests_VeTantoElCatalogoGlobalComoSuTestCustom()
    {
        // Act: PF B consulta el catálogo de tests
        var response = await _clientPfB.GetAsync("/api/v1/fitness-tests");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tests = await response.Content.ReadFromJsonAsync<List<FitnessTestDto>>();
        Assert.NotNull(tests);

        // PF B sí ve su propio test custom
        Assert.Contains(tests, t => t.Id == _customTestB.Id);
    }

    [Fact]
    public async Task PostInternalCheckInsDone_PfAConsultando_NoDevuelveCheckInsDePfB()
    {
        // En InitializeAsync ya se sembró un check-in para _pfBId con _sharedAthleteId en ArgentinaTime.Today (#KAN-528)
        var today = ArgentinaTime.Today;

        // Act 1: cliente con rol Servicio consultando con el pfId de PF A
        var serviceClient = _factory.CreateAuthenticatedClient(Guid.Empty, role: "Servicio");
        var requestPfA = new
        {
            Date = today,
            PfId = _pfAId,
            AthleteIds = new[] { _sharedAthleteId }
        };

        var responsePfA = await serviceClient.PostAsJsonAsync("/api/v1/internal/check-ins/done", requestPfA);

        // Assert: con el pfId de PF A no viene el check-in cargado para PF B
        Assert.Equal(HttpStatusCode.OK, responsePfA.StatusCode);
        var completedPfA = await responsePfA.Content.ReadFromJsonAsync<CheckInsDoneResponse>();
        Assert.NotNull(completedPfA);
        Assert.DoesNotContain(_sharedAthleteId, completedPfA.AthleteIds);

        // Act 2: cliente con rol Servicio consultando con el pfId de PF B
        var requestPfB = new
        {
            Date = today,
            PfId = _pfBId,
            AthleteIds = new[] { _sharedAthleteId }
        };

        var responsePfB = await serviceClient.PostAsJsonAsync("/api/v1/internal/check-ins/done", requestPfB);
        Assert.Equal(HttpStatusCode.OK, responsePfB.StatusCode);
        var completedPfB = await responsePfB.Content.ReadFromJsonAsync<CheckInsDoneResponse>();
        Assert.NotNull(completedPfB);
        Assert.Contains(_sharedAthleteId, completedPfB.AthleteIds);
    }
}