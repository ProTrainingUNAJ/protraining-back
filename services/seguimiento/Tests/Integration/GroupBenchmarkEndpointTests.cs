using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Application.DTOs.Benchmark;
using Application.Interfaces.Services;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class GroupBenchmarkEndpointTests
{
    private readonly Guid _athleteId = Guid.NewGuid();
    private readonly Guid _otherAthleteId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly Guid _secondCategoryId = Guid.NewGuid();
    private readonly Guid _foreignCategoryId = Guid.NewGuid();

    private sealed class TestFitnessMeasurementService : IFitnessTestMeasurementService
    {
        private readonly List<AthleteTestMeasurement> _measurements = new();

        public void Add(Guid athleteId, string testType, string unit, double value, DateTime takenAt)
        {
            _measurements.Add(new AthleteTestMeasurement(athleteId, testType, value, unit, takenAt));
        }

        public Task<IReadOnlyList<AthleteTestMeasurement>> GetMeasurementsByAthletesAsync(
            IReadOnlyCollection<Guid> athleteIds,
            CancellationToken cancellationToken = default)
        {
            var ids = athleteIds.ToHashSet();
            IReadOnlyList<AthleteTestMeasurement> matches = _measurements.Where(m => ids.Contains(m.AthleteId)).ToList();
            return Task.FromResult(matches);
        }
    }

    private void SetupFakeStructure(
        SeguimientoTestApplicationFactory factory,
        Guid authenticatedAthleteId,
        IReadOnlyList<(Guid Id, string Name)>? athleteCategories = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? categoryMemberships = null)
    {
        var cats = athleteCategories ?? new[] { (_categoryId, "Primera División") };
        var memberships = categoryMemberships ?? new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [_categoryId] = new[] { authenticatedAthleteId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
        };

        factory.Structure.Behavior = (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("api/v1/me", StringComparison.OrdinalIgnoreCase))
            {
                var meJson = JsonSerializer.Serialize(new
                {
                    athleteId = authenticatedAthleteId,
                    fullName = "Atleta de Prueba",
                    categories = cats.Select(c => new { id = c.Id, name = c.Name })
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(meJson, Encoding.UTF8, "application/json")
                });
            }

            foreach (var (catId, members) in memberships)
            {
                if (path.Contains($"categories/{catId}/athletes", StringComparison.OrdinalIgnoreCase))
                {
                    var athletesJson = JsonSerializer.Serialize(
                        members.Select(id => new { id, fullName = $"Atleta {id}" }));
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(athletesJson, Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
    }

    [Fact]
    public async Task KAN449_01_Given_AuthenticatedAthlete_When_QueriesOwnBenchmark_Then_ReturnsOwnNumbersAndCategoryAverage()
    {
        var testService = new TestFitnessMeasurementService();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();
        var peer3 = Guid.NewGuid();

        // Salto vertical: Atleta 30 cm; Compañeros: 34, 36, 40 cm -> Promedio categoría = (30+34+36+40)/4 = 35.0 cm
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow);
        testService.Add(peer1, "Salto Vertical CMJ", "cm", 34.0, DateTime.UtcNow);
        testService.Add(peer2, "Salto Vertical CMJ", "cm", 36.0, DateTime.UtcNow);
        testService.Add(peer3, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peer1, peer2, peer3 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);
        Assert.Equal(_athleteId, data.AthleteId);
        Assert.Equal(_categoryId, data.CategoryId);

        var metric = Assert.Single(data.Metrics);
        Assert.Equal("Salto Vertical CMJ (cm)", metric.Dimension);
        Assert.Equal(30.0, metric.AthleteValue);
        Assert.Equal(35.0, metric.GroupAverage);
        Assert.Equal("cm", metric.Unit);
        Assert.Equal(-5.0, metric.Difference);
        Assert.Equal(-14.3, metric.PercentageDifference);
        Assert.Equal("HigherIsBetter", metric.Direction);
        Assert.Equal("BelowAverage", metric.Status);
        Assert.True(metric.IsWeakestPoint);
        Assert.Contains("Por debajo del promedio", metric.Trend);
    }

    [Fact]
    public async Task KAN449_02_Given_AuthenticatedAthlete_When_QueriesOtherAthlete_Then_Returns403Forbidden()
    {
        var testService = new TestFitnessMeasurementService();
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId);

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");

        // El atleta _athleteId intenta consultar el perfil de _otherAthleteId
        var response = await client.GetAsync($"api/v1/athletes/{_otherAthleteId}/group-benchmark");

        // Requisito mandatorio KAN-449: HTTP 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task KAN449_03_Given_AuthenticatedAthlete_When_QueriesOwnBenchmark_Then_ResponseContainsZeroPeerDataOrIds()
    {
        var testService = new TestFitnessMeasurementService();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();
        var peer3 = Guid.NewGuid();

        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 32.0, DateTime.UtcNow);
        testService.Add(peer1, "Salto Vertical CMJ", "cm", 38.0, DateTime.UtcNow);
        testService.Add(peer2, "Salto Vertical CMJ", "cm", 36.0, DateTime.UtcNow);
        testService.Add(peer3, "Salto Vertical CMJ", "cm", 34.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peer1, peer2, peer3 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Validación estricta de privacidad: ningún ID ajeno, nombre de compañero o valor crudo ajeno
        Assert.DoesNotContain(peer1.ToString(), rawJson);
        Assert.DoesNotContain(peer2.ToString(), rawJson);
        Assert.DoesNotContain(peer3.ToString(), rawJson);
        Assert.DoesNotContain("Compañero", rawJson);
        Assert.DoesNotContain("38.0", rawJson);
    }

    [Fact]
    public async Task KAN449_04_Given_UnauthenticatedRequest_When_QueriesBenchmark_Then_Returns401Unauthorized()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();
        var client = factory.CreateClient(); // Sin token

        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KAN449_05_Given_AuthenticatedAthlete_When_QueriesForeignCategory_Then_Returns404NotFound()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();
        SetupFakeStructure(factory, _athleteId,
            athleteCategories: new[] { (_categoryId, "Primera División") });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");

        // Intenta consultar la categoría foránea _foreignCategoryId a la que no pertenece
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark?categoryId={_foreignCategoryId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task KAN449_06_Given_AthleteInMultipleCategories_When_QueriesSpecificCategory_Then_ReturnsAveragesOfThatCategory()
    {
        var testService = new TestFitnessMeasurementService();
        var res1 = Guid.NewGuid();
        var res2 = Guid.NewGuid();
        var res3 = Guid.NewGuid();

        var prim1 = Guid.NewGuid();
        var prim2 = Guid.NewGuid();
        var prim3 = Guid.NewGuid();

        // En Reserva (_categoryId): Atleta 35 cm, Compañeros 25, 27, 33 cm -> Promedio = (35+25+27+33)/4 = 30.0 cm
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 35.0, DateTime.UtcNow);
        testService.Add(res1, "Salto Vertical CMJ", "cm", 25.0, DateTime.UtcNow);
        testService.Add(res2, "Salto Vertical CMJ", "cm", 27.0, DateTime.UtcNow);
        testService.Add(res3, "Salto Vertical CMJ", "cm", 33.0, DateTime.UtcNow);

        // En Primera (_secondCategoryId): Atleta 35 cm, Compañeros 41, 41, 43 cm -> Promedio = (35+41+41+43)/4 = 40.0 cm
        testService.Add(prim1, "Salto Vertical CMJ", "cm", 41.0, DateTime.UtcNow);
        testService.Add(prim2, "Salto Vertical CMJ", "cm", 41.0, DateTime.UtcNow);
        testService.Add(prim3, "Salto Vertical CMJ", "cm", 43.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            athleteCategories: new[]
            {
                (_categoryId, "Reserva"),
                (_secondCategoryId, "Primera División")
            },
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, res1, res2, res3 },
                [_secondCategoryId] = new[] { _athleteId, prim1, prim2, prim3 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");

        // Consulta explícita a Primera División
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark?categoryId={_secondCategoryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);
        Assert.Equal(_secondCategoryId, data.CategoryId);
        Assert.Equal("Primera División", data.CategoryName);

        var metric = Assert.Single(data.Metrics);
        Assert.Equal(40.0, metric.GroupAverage);
        Assert.Equal(35.0, metric.AthleteValue);
        Assert.Equal(-5.0, metric.Difference);
        Assert.Equal("BelowAverage", metric.Status);
    }

    [Fact]
    public async Task KAN449_07_Given_TimedSprintTest_When_LowerTimeIsBetter_Then_CorrectDirectionAndStatusAssigned()
    {
        var testService = new TestFitnessMeasurementService();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();
        var peer3 = Guid.NewGuid();

        // Sprint 30m: Atleta 4.0 s, Compañeros 4.3, 4.4, 4.5 s -> Promedio categoría = (4.0+4.3+4.4+4.5)/4 = 4.3 s
        // Menor tiempo es mejor -> Atleta está más rápido (+7.0% ventaja)
        testService.Add(_athleteId, "Sprint 30m", "s", 4.0, DateTime.UtcNow);
        testService.Add(peer1, "Sprint 30m", "s", 4.3, DateTime.UtcNow);
        testService.Add(peer2, "Sprint 30m", "s", 4.4, DateTime.UtcNow);
        testService.Add(peer3, "Sprint 30m", "s", 4.5, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peer1, peer2, peer3 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);

        var sprintMetric = Assert.Single(data.Metrics);
        Assert.Equal("LowerIsBetter", sprintMetric.Direction);
        Assert.Equal("AboveAverage", sprintMetric.Status);
        Assert.True(sprintMetric.PercentageDifference > 0);
        Assert.Contains("Superior al promedio", sprintMetric.Trend);
    }

    [Fact]
    public async Task KAN449_08_Given_MultipleTestsWithDeficits_When_Evaluating_Then_WeakestPointHasMostNegativeRelativeDelta()
    {
        var testService = new TestFitnessMeasurementService();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();
        var peer3 = Guid.NewGuid();

        // Salto CMJ: Atleta 30 cm vs Promedio 35 cm (-14.3% déficit) -> Compañeros: 34, 36, 40
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow);
        testService.Add(peer1, "Salto Vertical CMJ", "cm", 34.0, DateTime.UtcNow);
        testService.Add(peer2, "Salto Vertical CMJ", "cm", 36.0, DateTime.UtcNow);
        testService.Add(peer3, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);

        // Fuerza Sentadilla: Atleta 90 kg vs Promedio 100 kg (-10.0% déficit) -> Compañeros: 100, 102, 108 -> (90+100+102+108)/4 = 100.0
        testService.Add(_athleteId, "1RM Sentadilla", "kg", 90.0, DateTime.UtcNow);
        testService.Add(peer1, "1RM Sentadilla", "kg", 100.0, DateTime.UtcNow);
        testService.Add(peer2, "1RM Sentadilla", "kg", 102.0, DateTime.UtcNow);
        testService.Add(peer3, "1RM Sentadilla", "kg", 108.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peer1, peer2, peer3 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);
        Assert.Equal(2, data.Metrics.Count);

        var jumpMetric = data.Metrics.First(m => m.Dimension.Contains("Salto"));
        var squatMetric = data.Metrics.First(m => m.Dimension.Contains("Sentadilla"));

        // El salto (-14.3%) es más desfavorable que la sentadilla (-10.0%) -> weakest point
        Assert.True(jumpMetric.IsWeakestPoint);
        Assert.False(squatMetric.IsWeakestPoint);
    }

    [Fact]
    public async Task KAN449_09_Given_AthleteWithoutMeasurements_When_QueriesBenchmark_Then_ReturnsEmptyMetrics()
    {
        var testService = new TestFitnessMeasurementService();
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId);

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);
        Assert.Empty(data.Metrics);
    }

    [Fact]
    public async Task KAN449_10_Given_ValuesWithinNeutralThreshold_When_Evaluated_Then_MarkedAsNeutral()
    {
        var testService = new TestFitnessMeasurementService();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();
        var peer3 = Guid.NewGuid();

        // 35.5 cm vs promedio 35.0 cm (+1.4% de diferencia, dentro de +-2%)
        // Atleta: 35.5; Compañeros: 34.5, 35.0, 35.0 -> (35.5 + 34.5 + 35.0 + 35.0) / 4 = 35.0
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 35.5, DateTime.UtcNow);
        testService.Add(peer1, "Salto Vertical CMJ", "cm", 34.5, DateTime.UtcNow);
        testService.Add(peer2, "Salto Vertical CMJ", "cm", 35.0, DateTime.UtcNow);
        testService.Add(peer3, "Salto Vertical CMJ", "cm", 35.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peer1, peer2, peer3 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);

        var metric = Assert.Single(data.Metrics);
        Assert.Equal("Neutral", metric.Status);
        Assert.False(metric.IsWeakestPoint);
    }

    [Fact]
    public async Task KAN449_11_Given_LessThanThreePeersWithData_When_QueriesOwnBenchmark_Then_ReturnsInsufficientGroupDataAndNullAverage()
    {
        var testService = new TestFitnessMeasurementService();
        var peer1 = Guid.NewGuid();
        var peer2 = Guid.NewGuid();

        // Solo 2 compañeros tienen mediciones (umbral k-anonimato requiere >= 3 compañeros)
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow);
        testService.Add(peer1, "Salto Vertical CMJ", "cm", 35.0, DateTime.UtcNow);
        testService.Add(peer2, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peer1, peer2 }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<GroupBenchmarkResponseDto>();
        Assert.NotNull(data);

        var metric = Assert.Single(data.Metrics);
        // Debe conservar el valor del propio atleta
        Assert.Equal(30.0, metric.AthleteValue);
        // Promedio, diferencia, porcentaje y tendencia deben ser NULL para proteger privacidad
        Assert.Null(metric.GroupAverage);
        Assert.Null(metric.Difference);
        Assert.Null(metric.PercentageDifference);
        Assert.Null(metric.Trend);
        Assert.Equal("InsufficientGroupData", metric.Status);
        Assert.False(metric.IsWeakestPoint);
    }

    [Fact]
    public async Task KAN449_12_Given_PFUser_When_CallsGroupBenchmark_Then_Returns403Forbidden()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync();
        SetupFakeStructure(factory, _athleteId);

        // Cliente autenticado con rol PreparadorFisico (endpoint exclusivo de Atleta)
        var client = factory.CreateAuthenticatedClient(_athleteId, "PreparadorFisico");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
