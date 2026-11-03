using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Averages;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

[Trait("Category", "Isolation")]
public sealed class CategoryAveragesEndpointTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    private static string Endpoint(Guid categoryId, string? weekStart = null) =>
        string.IsNullOrEmpty(weekStart)
            ? $"api/v1/categories/{categoryId}/averages"
            : $"api/v1/categories/{categoryId}/averages?weekStart={weekStart}";

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

    [Fact]
    public async Task Given_CategoryWith10AthletesAndMeasurements_When_GetAverages_Then_ReturnsCorrectArithmeticMean()
    {
        var testService = new TestFitnessMeasurementService();
        var athleteIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in athleteIds)
        {
            testService.Add(id, "Salto Vertical CMJ", "cm", 35.5, DateTime.UtcNow);
        }

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(
            athleteIds.Select(id => new { id, fullName = $"Atleta {id}" })));

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);
        Assert.Equal(_categoryId, averages.CategoryId);
        Assert.Equal(10, averages.AthleteCount);

        var testAvg = Assert.Single(averages.FitnessTests);
        Assert.Equal("Salto Vertical CMJ", testAvg.TestName);
        Assert.Equal("cm", testAvg.Unit);
        Assert.Equal(35.5, testAvg.Average);
        Assert.Equal(10, testAvg.TestedAthleteCount);
    }

    [Fact]
    public async Task Given_AthletesOutsideCategory_When_GetAverages_Then_ExcludedFromCalculation()
    {
        var testService = new TestFitnessMeasurementService();
        var inCategoryAthlete = Guid.NewGuid();
        var outsideAthlete = Guid.NewGuid();

        testService.Add(inCategoryAthlete, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);
        testService.Add(outsideAthlete, "Salto Vertical CMJ", "cm", 20.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = inCategoryAthlete, fullName = "Atleta de Categoria" }
        }));

        await SeedCheckInAsync(factory, inCategoryAthlete, new DateTime(2026, 10, 28), sleepHours: 8);
        await SeedCheckInAsync(factory, outsideAthlete, new DateTime(2026, 10, 28), sleepHours: 2);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId, "2026-10-26"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);
        Assert.Equal(1, averages.AthleteCount);

        var testAvg = Assert.Single(averages.FitnessTests);
        Assert.Equal(40.0, testAvg.Average);
        Assert.Equal(1, testAvg.TestedAthleteCount);

        var sleepAvg = averages.WellnessMetrics.FirstOrDefault(m => m.Metric == "SleepHours");
        Assert.NotNull(sleepAvg);
        Assert.Equal(8.0, sleepAvg.Average);
        Assert.Equal(1, sleepAvg.ReportedAthleteCount);
    }

    [Fact]
    public async Task Given_EmptyCategory_When_GetAverages_Then_Returns200WithEmptyAveragesAndZeroCount()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWithAthletes("[]");

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);
        Assert.Equal(0, averages.AthleteCount);
        Assert.Empty(averages.FitnessTests);
        Assert.Empty(averages.WellnessMetrics);
    }

    [Fact]
    public async Task Given_AthletesWithoutMeasurements_When_GetAverages_Then_NotCountedAsZeroInDivisor()
    {
        var testService = new TestFitnessMeasurementService();
        var athleteWithTest = Guid.NewGuid();
        var athleteWithoutTest = Guid.NewGuid();

        testService.Add(athleteWithTest, "Salto Vertical CMJ", "cm", 50.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athleteWithTest, fullName = "Atleta Con Test" },
            new { id = athleteWithoutTest, fullName = "Atleta Sin Test" }
        }));

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);
        Assert.Equal(2, averages.AthleteCount);

        var testAvg = Assert.Single(averages.FitnessTests);
        Assert.Equal(50.0, testAvg.Average);
        Assert.Equal(1, testAvg.TestedAthleteCount);
    }

    [Fact]
    public async Task Given_MultipleTestTypes_When_GetAverages_Then_CalculatedSeparately()
    {
        var testService = new TestFitnessMeasurementService();
        var athlete1 = Guid.NewGuid();
        var athlete2 = Guid.NewGuid();

        testService.Add(athlete1, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow);
        testService.Add(athlete2, "Salto Vertical CMJ", "cm", 40.0, DateTime.UtcNow);

        testService.Add(athlete1, "Sprint 30m", "seg", 4.2, DateTime.UtcNow);
        testService.Add(athlete2, "Sprint 30m", "seg", 4.4, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athlete1, fullName = "Atleta 1" },
            new { id = athlete2, fullName = "Atleta 2" }
        }));

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);
        Assert.Equal(2, averages.FitnessTests.Count);

        var jump = averages.FitnessTests.First(t => t.TestName == "Salto Vertical CMJ");
        Assert.Equal(35.0, jump.Average);
        Assert.Equal("cm", jump.Unit);
        Assert.Equal(2, jump.TestedAthleteCount);

        var sprint = averages.FitnessTests.First(t => t.TestName == "Sprint 30m");
        Assert.Equal(4.3, sprint.Average);
        Assert.Equal("seg", sprint.Unit);
        Assert.Equal(2, sprint.TestedAthleteCount);
    }

    [Fact]
    public async Task Given_AthleteWithMultipleMeasurements_When_GetAverages_Then_UsesLatestValidMeasurement()
    {
        var testService = new TestFitnessMeasurementService();
        var athlete = Guid.NewGuid();

        testService.Add(athlete, "Salto Vertical CMJ", "cm", 30.0, DateTime.UtcNow.AddDays(-5));
        testService.Add(athlete, "Salto Vertical CMJ", "cm", 38.0, DateTime.UtcNow.AddDays(-1));

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athlete, fullName = "Atleta Multiple" }
        }));

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);

        var testAvg = Assert.Single(averages.FitnessTests);
        Assert.Equal(38.0, testAvg.Average);
        Assert.Equal(1, testAvg.TestedAthleteCount);
    }

    [Fact]
    public async Task Given_Measurements_When_AverageCalculated_Then_RoundedToOneDecimalPlace()
    {
        var testService = new TestFitnessMeasurementService();
        var athlete1 = Guid.NewGuid();
        var athlete2 = Guid.NewGuid();

        // (35.5 + 36.0) / 2 = 35.75 -> 35.8
        testService.Add(athlete1, "CMJ", "cm", 35.5, DateTime.UtcNow);
        testService.Add(athlete2, "CMJ", "cm", 36.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athlete1, fullName = "A1" },
            new { id = athlete2, fullName = "A2" }
        }));

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);
        var testAvg = Assert.Single(averages.FitnessTests);
        Assert.Equal(35.8, testAvg.Average);
    }

    [Fact]
    public async Task Given_CheckInsInsideAndOutsideWeek_When_GetAverages_Then_OnlyConsidersWeekCheckIns()
    {
        var athlete = Guid.NewGuid();
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athlete, fullName = "Atleta Semanal" }
        }));

        // Semana seleccionada: 2026-10-19 (Lunes) a 2026-10-25 (Domingo)
        // CheckIn antes (domingo 18)
        await SeedCheckInAsync(factory, athlete, new DateTime(2026, 10, 18), sleepHours: 2);
        // CheckIn adentro (martes 20)
        await SeedCheckInAsync(factory, athlete, new DateTime(2026, 10, 20), sleepHours: 8);
        // CheckIn después (lunes 26)
        await SeedCheckInAsync(factory, athlete, new DateTime(2026, 10, 26), sleepHours: 4);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId, "2026-10-19"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);

        var sleepAvg = averages.WellnessMetrics.FirstOrDefault(m => m.Metric == "SleepHours");
        Assert.NotNull(sleepAvg);
        Assert.Equal(8.0, sleepAvg.Average);
        Assert.Equal(1, sleepAvg.TotalCheckIns);
        Assert.Equal(1, sleepAvg.ReportedAthleteCount);
    }

    [Fact]
    public async Task Given_MultipleCheckInsInWeekForAthlete_When_GetAverages_Then_AveragedProperly()
    {
        var athlete1 = Guid.NewGuid();
        var athlete2 = Guid.NewGuid();

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athlete1, fullName = "A1" },
            new { id = athlete2, fullName = "A2" }
        }));

        // Semana: 2026-10-26 (Lunes) a 2026-11-01 (Domingo)
        // A1 reporta lunes (8) y miércoles (6) -> 14 / 2 = 7
        // A2 reporta martes (10)
        // Total checkins = 3, sum = 24 / 3 = 8.0
        await SeedCheckInAsync(factory, athlete1, new DateTime(2026, 10, 26), sleepHours: 8, fatigueLevel: 2);
        await SeedCheckInAsync(factory, athlete1, new DateTime(2026, 10, 28), sleepHours: 6, fatigueLevel: 4);
        await SeedCheckInAsync(factory, athlete2, new DateTime(2026, 10, 27), sleepHours: 10, fatigueLevel: 3);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId, "2026-10-26"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);

        var sleepAvg = averages.WellnessMetrics.FirstOrDefault(m => m.Metric == "SleepHours");
        Assert.NotNull(sleepAvg);
        Assert.Equal(8.0, sleepAvg.Average);
        Assert.Equal(3, sleepAvg.TotalCheckIns);
        Assert.Equal(2, sleepAvg.ReportedAthleteCount);

        var fatigueAvg = averages.WellnessMetrics.FirstOrDefault(m => m.Metric == "FatigueLevel");
        Assert.NotNull(fatigueAvg);
        // (2 + 4 + 3) / 3 = 3.0
        Assert.Equal(3.0, fatigueAvg.Average);
        Assert.Equal(3, fatigueAvg.TotalCheckIns);
        Assert.Equal(2, fatigueAvg.ReportedAthleteCount);
    }

    [Fact]
    public async Task Given_CheckInsWithNullOrMissingEffortRate_When_GetAverages_Then_OnlyValidValuesCountedInDivisor()
    {
        var athlete1 = Guid.NewGuid();
        var athlete2 = Guid.NewGuid();

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athlete1, fullName = "A1" },
            new { id = athlete2, fullName = "A2" }
        }));

        // Athlete 1 reporta EffortRate = 4
        await SeedCheckInAsync(factory, athlete1, new DateTime(2026, 10, 27), effortRate: 4);
        // Athlete 2 reporta EffortRate = null
        await SeedCheckInAsync(factory, athlete2, new DateTime(2026, 10, 27), effortRate: null);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId, "2026-10-26"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var averages = await response.Content.ReadFromJsonAsync<CategoryAveragesDto>();
        Assert.NotNull(averages);

        var effortAvg = averages.WellnessMetrics.FirstOrDefault(m => m.Metric == "EffortRate");
        Assert.NotNull(effortAvg);
        Assert.Equal(4.0, effortAvg.Average);
        Assert.Equal(1, effortAvg.TotalCheckIns);
        Assert.Equal(1, effortAvg.ReportedAthleteCount);
    }

    [Fact]
    public async Task Given_OtherPfCategoryOrNotFoundInStructure_When_GetAverages_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.NotFound, """{"title":"Categoría no encontrada."}""");

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Given_SuccessfulResponse_When_InspectingBody_Then_ZeroIndividualAthleteDataOrIdsExposed()
    {
        var athleteId = Guid.NewGuid();
        var athleteName = "Juan Perez Privado";
        var testService = new TestFitnessMeasurementService();

        testService.Add(athleteId, "Test Resistencia", "min", 15.0, DateTime.UtcNow);

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId, testService);
        factory.Structure.RespondWithAthletes(JsonSerializer.Serialize(new[]
        {
            new { id = athleteId, fullName = athleteName }
        }));

        await SeedCheckInAsync(factory, athleteId, new DateTime(2026, 10, 28), sleepHours: 9);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId, "2026-10-26"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();

        // Garantía estricta de privacidad: NUNCA se expone id, nombre o mediciones de un atleta individual.
        Assert.DoesNotContain(athleteId.ToString(), rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(athleteName, rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("individual", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_UnauthenticatedRequest_When_GetAverages_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateClient().GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRole_When_GetAverages_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_StructureServiceUnavailable_When_GetAverages_Then_Returns503()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        // Handler por defecto simula servicio caído (conexión rechazada)
        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static Task SeedCheckInAsync(
        SeguimientoTestApplicationFactory factory,
        Guid athleteId,
        DateTime date,
        double sleepHours = 8,
        int sleepQuality = 4,
        int muscleSoreness = 1,
        int fatigueLevel = 2,
        int moodRate = 4,
        int stressLevel = 2,
        int? effortRate = 4) =>
        factory.ExecuteInDbContextAsync(db =>
        {
            db.CheckIns.Add(new CheckIn
            {
                AthleteId = athleteId,
                Date = date,
                TrafficLight = TrafficLight.Green,
                TrafficLightReason = "OK",
                SleepHours = sleepHours,
                SleepQuality = sleepQuality,
                MuscleSoreness = muscleSoreness,
                FatigueLevel = fatigueLevel,
                MoodRate = moodRate,
                StressLevel = stressLevel,
                IsSick = false,
                EffortRate = effortRate,
                HasActiveInjury = false
            });
            return db.SaveChangesAsync();
        });
}
