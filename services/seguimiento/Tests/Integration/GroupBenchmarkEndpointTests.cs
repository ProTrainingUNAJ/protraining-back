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
            [_categoryId] = new[] { authenticatedAthleteId, Guid.NewGuid(), Guid.NewGuid() }
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
        var peerId = Guid.NewGuid();

        // Salto vertical: Atleta tiene 30 cm, Compañero tiene 40 cm -> Promedio de grupo = 35.0 cm
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow);
        testService.Add(peerId, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peerId }
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
    public async Task KAN449_02_Given_AuthenticatedAthlete_When_QueriesOtherAthlete_Then_Returns404NotFoundNever403()
    {
        var testService = new TestFitnessMeasurementService();
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId);

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");

        // El atleta _athleteId intenta consultar el perfil de _otherAthleteId
        var response = await client.GetAsync($"api/v1/athletes/{_otherAthleteId}/group-benchmark");

        // Requisito mandatorio KAN-450: 404 Not Found, NUNCA 403
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task KAN449_03_Given_AuthenticatedAthlete_When_QueriesOwnBenchmark_Then_ResponseContainsZeroPeerDataOrIds()
    {
        var testService = new TestFitnessMeasurementService();
        var peerId = Guid.NewGuid();

        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 32.0, DateTime.UtcNow);
        testService.Add(peerId, "Salto Vertical CMJ", "cm", 38.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peerId }
            });

        var client = factory.CreateAuthenticatedClient(_athleteId, "Atleta");
        var response = await client.GetAsync($"api/v1/athletes/{_athleteId}/group-benchmark");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Validación estricta de privacidad: ningún ID ajeno, nombre de compañero o valor crudo ajeno
        Assert.DoesNotContain(peerId.ToString(), rawJson);
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
        var resId1 = Guid.NewGuid();
        var primId1 = Guid.NewGuid();

        // En Reserva (_categoryId): promedio = 30 cm
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 35.0, DateTime.UtcNow);
        testService.Add(resId1, "Salto Vertical CMJ", "cm", 25.0, DateTime.UtcNow);

        // En Primera (_secondCategoryId): promedio = 40 cm
        testService.Add(primId1, "Salto Vertical CMJ", "cm", 45.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            athleteCategories: new[]
            {
                (_categoryId, "Reserva"),
                (_secondCategoryId, "Primera División")
            },
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, resId1 },
                [_secondCategoryId] = new[] { _athleteId, primId1 }
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
        Assert.Equal(40.0, metric.GroupAverage); // (35 + 45) / 2 = 40.0
        Assert.Equal(35.0, metric.AthleteValue);
        Assert.Equal(-5.0, metric.Difference);
        Assert.Equal("BelowAverage", metric.Status);
    }

    [Fact]
    public async Task KAN449_07_Given_TimedSprintTest_When_LowerTimeIsBetter_Then_CorrectDirectionAndStatusAssigned()
    {
        var testService = new TestFitnessMeasurementService();
        var peerId = Guid.NewGuid();

        // Sprint 30m: Atleta tiene 4.0 s, Compañero tiene 4.6 s -> Promedio = 4.3 s
        // Menor tiempo es mejor -> Atleta está más rápido (+7.0% ventaja)
        testService.Add(_athleteId, "Sprint 30m", "s", 4.0, DateTime.UtcNow);
        testService.Add(peerId, "Sprint 30m", "s", 4.6, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peerId }
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
        var peerId = Guid.NewGuid();

        // Salto CMJ: Atleta 30 cm vs Promedio 35 cm (-14.3% déficit)
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow);
        testService.Add(peerId, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);

        // Fuerza Sentadilla: Atleta 90 kg vs Promedio 100 kg (-10.0% déficit)
        testService.Add(_athleteId, "1RM Sentadilla", "kg", 90.0, DateTime.UtcNow);
        testService.Add(peerId, "1RM Sentadilla", "kg", 110.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peerId }
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
        var peerId = Guid.NewGuid();

        // 35.5 cm vs promedio 35.0 cm (+1.4% de diferencia, dentro de +-2%)
        testService.Add(_athleteId, "Salto Vertical CMJ", "cm", 35.5, DateTime.UtcNow);
        testService.Add(peerId, "Salto Vertical CMJ", "cm", 34.5, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(customMeasurementService: testService);
        SetupFakeStructure(factory, _athleteId,
            categoryMemberships: new Dictionary<Guid, IReadOnlyList<Guid>>
            {
                [_categoryId] = new[] { _athleteId, peerId }
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
}

