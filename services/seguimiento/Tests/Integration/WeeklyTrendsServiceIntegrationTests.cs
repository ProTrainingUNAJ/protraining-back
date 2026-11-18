using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using API.Services;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.Trends.GetCategoryWeeklyTrends;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Seguimiento.Tests.Infrastructure;
using Testcontainers.PostgreSql;

namespace Seguimiento.Tests.Integration;

/// <summary>
/// Integración real entre microservicios para KAN-489 (#194):
/// consulta Estructura real por HTTP y PostgreSQL 16 real en Testcontainers para Seguimiento.
/// No utiliza mocks de servicios externos. Requiere: docker compose up -d --wait estructura.
/// </summary>
[Trait("Category", "ServiceIntegration")]
[Trait("Category", "RequiresDocker")]
[Trait("Category", "Isolation")]
public sealed class WeeklyTrendsServiceIntegrationTests : IAsyncLifetime
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubServiceTokenFactory(string token) : IServiceTokenFactory
    {
        public string CreateToken() => token;
    }

    private readonly ServiceIntegrationSettings _settings = ServiceIntegrationSettings.Load();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _settings.EnsureEstructuraAvailableAsync();
        await _postgres.StartAsync();

        await using var context = CreateContext(new TestCurrentUser(Guid.NewGuid()));
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task KAN489_ServiceIntegration_RealEstructuraAndPostgres_ComputesCategoryTrendsAndEnforcesPfIsolation()
    {
        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var fixedClock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 17, 18, 0, 0, TimeSpan.Zero));

        try
        {
            // 1. Crear organización, categoría y atleta en Estructura real vía HTTP con token de PF A.
            using var pfAClient = CreateAuthenticatedClient(_settings.EstructuraUrl, pfA, "PreparadorFisico");
            var orgId = await PostAndReadIdAsync(pfAClient, "api/v1/organizations", new { name = "Club K489", description = "Test KAN-489" });
            var categoryId = await PostAndReadIdAsync(pfAClient, "api/v1/categories", new { organizationId = orgId, name = "Primera K489", description = "Test KAN-489" });
            var athleteId = await PostAndReadIdAsync(pfAClient, "api/v1/athletes", new
            {
                firstName = "Julian",
                lastName = "Alvarez",
                email = $"kan489.{Guid.NewGuid():N}@test.local",
                phone = "+5491122334455",
                isMinor = false,
                allowsPhysicalTraining = true,
                allowsHabitsRecommendations = true
            });
            using var assignRes = await pfAClient.PostAsync($"api/v1/athletes/{athleteId}/categories/{categoryId}", null);
            assignRes.EnsureSuccessStatusCode();

            // 2. Persistir 4 semanas de check-ins y carga diaria en PostgreSQL real de Seguimiento bajo PF A.
            await using (var seedA = CreateContext(new TestCurrentUser(pfA, role: "PreparadorFisico")))
            {
                DateOnly[] checkInDates =
                [
                    new(2026, 10, 27),
                    new(2026, 11, 4),
                    new(2026, 11, 11),
                    new(2026, 11, 17)
                ];

                foreach (var date in checkInDates)
                {
                    seedA.CheckIns.Add(new CheckIn
                    {
                        AthleteId = athleteId,
                        Date = date.ToDateTime(TimeOnly.MinValue),
                        TrafficLight = TrafficLight.Green,
                        SleepHours = 8.0,
                        SleepQuality = 4,
                        MuscleSoreness = 2,
                        FatigueLevel = 2,
                        MoodRate = 4,
                        StressLevel = 2
                    });
                }

                seedA.DailyLoads.Add(DailyLoad.Register(athleteId, new DateOnly(2026, 10, 28).ToDateTime(TimeOnly.MinValue), 60, 5, 0, null, 0, null, 0, null, 0, null)); // 300
                seedA.DailyLoads.Add(DailyLoad.Register(athleteId, new DateOnly(2026, 11, 5).ToDateTime(TimeOnly.MinValue), 80, 6, 0, null, 0, null, 0, null, 0, null));  // 480
                seedA.DailyLoads.Add(DailyLoad.Register(athleteId, new DateOnly(2026, 11, 12).ToDateTime(TimeOnly.MinValue), 50, 7, 0, null, 0, null, 0, null, 0, null)); // 350
                seedA.DailyLoads.Add(DailyLoad.Register(athleteId, new DateOnly(2026, 11, 17).ToDateTime(TimeOnly.MinValue), 45, 6, 0, null, 0, null, 0, null, 0, null)); // 270
                await seedA.SaveChangesAsync();
            }

            // 3. Consultar tendencias grupales como PF A usando StructureClient HTTP real.
            var pfAUser = new TestCurrentUser(pfA, role: "PreparadorFisico");
            await using var readContextA = CreateContext(pfAUser);
            using var estructuraHttpA = CreateAuthenticatedClient(_settings.EstructuraUrl, pfA, "PreparadorFisico");
            var structureClientA = new StructureClient(
                estructuraHttpA,
                NullLogger<StructureClient>.Instance,
                new StubServiceTokenFactory(CreateToken(Guid.NewGuid(), "Servicio")));

            var handlerA = new GetCategoryWeeklyTrendsQueryHandler(
                structureClientA,
                new CheckInRepository(readContextA),
                new DailyLoadRepository(readContextA),
                fixedClock);

            var trends = await handlerA.HandleAsync(new GetCategoryWeeklyTrendsQuery(categoryId, Weeks: 4));

            Assert.Equal(categoryId, trends.CategoryId);
            Assert.Equal(1, trends.AthleteCount);
            Assert.Equal(4, trends.Weeks.Count);
            Assert.Equal(4.0, trends.Weeks[0].AverageWellness);
            Assert.Equal(300, trends.Weeks[0].TotalLoad);
            Assert.Equal(480, trends.Weeks[1].TotalLoad);
            Assert.Equal(350, trends.Weeks[2].TotalLoad);
            Assert.Equal(270, trends.Weeks[3].TotalLoad);

            // 4. PF B intenta consultar la misma categoría -> Estructura real responde 404 -> CategoryNotFoundException.
            var pfBUser = new TestCurrentUser(pfB, role: "PreparadorFisico");
            await using var readContextB = CreateContext(pfBUser);
            using var estructuraHttpB = CreateAuthenticatedClient(_settings.EstructuraUrl, pfB, "PreparadorFisico");
            var structureClientB = new StructureClient(
                estructuraHttpB,
                NullLogger<StructureClient>.Instance,
                new StubServiceTokenFactory(CreateToken(Guid.NewGuid(), "Servicio")));

            var handlerB = new GetCategoryWeeklyTrendsQueryHandler(
                structureClientB,
                new CheckInRepository(readContextB),
                new DailyLoadRepository(readContextB),
                fixedClock);

            await Assert.ThrowsAsync<CategoryNotFoundException>(() =>
                handlerB.HandleAsync(new GetCategoryWeeklyTrendsQuery(categoryId, Weeks: 4)));
        }
        finally
        {
            await CleanupEstructuraTenantAsync(pfA);
        }
    }

    private AppDbContext CreateContext(TestCurrentUser currentUser) =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options,
            currentUser);

    private HttpClient CreateAuthenticatedClient(Uri baseUrl, Guid subjectId, string role)
    {
        var client = new HttpClient { BaseAddress = baseUrl };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(subjectId, role));
        return client;
    }

    private string CreateToken(Guid subjectId, string role)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new(JwtRegisteredClaimNames.Name, $"Test KAN-489 {subjectId:N}"),
            new("role", role)
        };
        if (role == "PreparadorFisico")
            claims.Add(new Claim("pf_id", subjectId.ToString()));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.JwtKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_settings.JwtIssuer, _settings.JwtAudience, claims,
            expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<Guid> PostAndReadIdAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Estructura respondió {(int)response.StatusCode} en POST {url}: {await response.Content.ReadAsStringAsync()}");
        }
        var json = await response.Content.ReadFromJsonAsync<JsonObject>();
        return json!["id"]!.GetValue<Guid>();
    }

    private async Task CleanupEstructuraTenantAsync(Guid pfId)
    {
        string[] tablesInDeleteOrder =
        [
            "competitive_events", "season_phases", "category_invitations", "athlete_categories",
            "medical_record_audit_logs", "medical_records", "digital_consents", "athlete_photos",
            "push_subscriptions", "pending_purges", "athletes", "categories",
            "organization_photos", "organizations"
        ];

        await using var estructura = new NpgsqlConnection(_settings.EstructuraDbConnectionString);
        await estructura.OpenAsync();
        await using var transaction = await estructura.BeginTransactionAsync();
        foreach (var table in tablesInDeleteOrder)
        {
            await using var delete = new NpgsqlCommand($"DELETE FROM {table} WHERE pf_id = @pfId", estructura, transaction);
            delete.Parameters.AddWithValue("pfId", pfId);
            await delete.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private sealed record ServiceIntegrationSettings(
        Uri EstructuraUrl,
        string EstructuraDbConnectionString,
        string JwtKey,
        string JwtIssuer,
        string JwtAudience)
    {
        public static ServiceIntegrationSettings Load()
        {
            var fileValues = LoadEnvFile();
            string Get(string key, string? fallback = null) =>
                Environment.GetEnvironmentVariable(key)
                ?? (fileValues.TryGetValue(key, out var value) ? value : null)
                ?? fallback
                ?? throw new InvalidOperationException(
                    $"Falta la variable '{key}' para las pruebas de integración.");

            var dbUser = Get("POSTGRES_USER");
            var dbPassword = Get("POSTGRES_PASSWORD");
            var dbHost = Get("PROTRAINING_TEST_DB_HOST", "localhost");
            var dbPort = Get("PROTRAINING_TEST_ESTRUCTURA_DB_PORT", "5434");

            return new ServiceIntegrationSettings(
                new Uri(Get("PROTRAINING_TEST_ESTRUCTURA_URL", "http://localhost:8082/")),
                $"Host={dbHost};Port={dbPort};Database=estructura;Username={dbUser};Password={dbPassword}",
                Get("JWT_KEY"),
                Get("JWT_ISSUER"),
                Get("JWT_AUDIENCE"));
        }

        public async Task EnsureEstructuraAvailableAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await http.GetAsync(new Uri(EstructuraUrl, "health"));
            response.EnsureSuccessStatusCode();

            await using var connection = new NpgsqlConnection(EstructuraDbConnectionString);
            await connection.OpenAsync();
        }

        private static Dictionary<string, string> LoadEnvFile()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
                directory = directory.Parent;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (directory is null)
                return values;

            var envFile = Path.Combine(directory.FullName, ".env");
            if (!File.Exists(envFile))
                envFile = Path.Combine(directory.FullName, ".env.example");
            if (!File.Exists(envFile))
                return values;

            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;
                var separator = trimmed.IndexOf('=');
                if (separator > 0)
                    values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
            }
            return values;
        }
    }
}
