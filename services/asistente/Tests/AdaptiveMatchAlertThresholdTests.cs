using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using API.Services;
using Application.DTOs.Alerts;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.Alerts.GetAlertById;
using Application.UseCases.Alerts.GetAlerts;
using Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Data.Repositories;
using Infrastructure.Services.Seguimiento;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Asistente.Tests;

/// <summary>
/// Integración real entre microservicios para KAN-477 (#182): umbral adaptativo por partido próximo (D0..D+3).
/// Usa Estructura y Seguimiento reales (levantados con docker compose) y PostgreSQL real de Asistente (Testcontainers).
/// No usa mocks de servicios externos. Cada prueba crea su propio PF (tenant) y lo elimina al finalizar.
/// Requiere: docker compose up -d --wait estructura seguimiento (ver job "service-integration" del CI).
/// </summary>
[Trait("Category", "ServiceIntegration")]
[Trait("Category", "RequiresDocker")]
public sealed class AdaptiveMatchAlertThresholdTests(ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly DateOnly Wednesday = new(2026, 11, 4);
    private static readonly DateOnly Saturday = new(2026, 11, 7);

    private readonly ServiceIntegrationSettings _settings = ServiceIntegrationSettings.Load();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        // Sin servicios no hay prueba: se falla con un mensaje explícito en lugar de omitirla.
        await _settings.EnsureServicesAvailableAsync();

        await _postgres.StartAsync();
        await using var context = CreateContext(new CurrentUser(new HttpContextAccessor()));
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task CA_AcceptanceCriteria_WedAnalysis_SatMatch_TwoHardSessions_RealInfrastructure()
    {
        await RunInTestTenantAsync(async tenant =>
        {
            // DADO: análisis el miércoles 04/11/2026, partido el sábado 07/11/2026 (D+3)
            // y dos sesiones de esfuerzo alto (RPE 8 y RPE 9).
            using var pfClient = CreateClient(_settings.EstructuraUrl, tenant.PfId, "PreparadorFisico");
            var organizationId = await CreateOrganizationAsync(pfClient, "Club Real K477");
            var categoryId = await CreateCategoryAsync(pfClient, organizationId, "Primera Division");
            var athleteId = await CreateAthleteAsync(pfClient, "Lautaro", "Martinez");
            tenant.AthleteIds.Add(athleteId);
            await AssignCategoryAsync(pfClient, athleteId, categoryId);
            await CreateMatchEventAsync(pfClient, categoryId, Saturday);

            await InsertCheckInAsync(athleteId, tenant.PfId, new DateOnly(2026, 11, 2), effortRate: 8);
            await InsertCheckInAsync(athleteId, tenant.PfId, new DateOnly(2026, 11, 3), effortRate: 9);

            var currentUser = new CurrentUser(new HttpContextAccessor());
            await using var context = CreateContext(currentUser);
            var alertRepository = new PreventiveAlertRepository(context);
            using var estructuraClient = CreateClient(_settings.EstructuraUrl, Guid.NewGuid(), "Servicio");
            using var seguimientoClient = CreateClient(_settings.SeguimientoUrl, Guid.NewGuid(), "Servicio");
            var handler = CreateHandler(estructuraClient, seguimientoClient, alertRepository, context, currentUser);

            // CUANDO: se ejecuta el análisis dentro de la ventana de proximidad.
            var firstRun = await handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(Wednesday));

            // ENTONCES: el aviso queda persistido en PostgreSQL para el PF responsable.
            Assert.True(firstRun.GeneratedAlerts >= 1);
            var alert = await context.PreventiveAlerts.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(a => a.AthleteId == athleteId);
            Assert.Equal(tenant.PfId, alert.PfId);
            Assert.Equal(categoryId, alert.CategoryId);
            Assert.Equal(AlertType.AcuteOverload, alert.Type);
            Assert.Equal(AlertSeverity.High, alert.Severity);
            Assert.Equal(SuggestedAdjustmentType.LoadReduction, alert.SuggestedAdjustmentType);
            Assert.Equal(20, alert.SuggestedAdjustmentPercentage);
            Assert.Contains("El atleta acumula 2 sesiones de esfuerzo alto y tiene un partido próximo", alert.Reason);
            Assert.Contains("07/11", alert.Reason);

            // Segunda ejecución: el aviso del atleta se omite como duplicado. Se verifica por atleta
            // y no por totales, porque Estructura compartida puede contener atletas de otros PF.
            context.ChangeTracker.Clear();
            var secondRun = await handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(Wednesday));
            Assert.True(secondRun.SkippedAlerts >= 1);
            Assert.Equal(1, await context.PreventiveAlerts.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(a => a.AthleteId == athleteId));

            // El PF responsable puede consultar el aviso con la información del riesgo precompetitivo.
            var getAlerts = new GetAlertsQueryHandler(alertRepository);
            var getAlertById = new GetAlertByIdQueryHandler(alertRepository);
            using (currentUser.UsePfContext(tenant.PfId))
            {
                var visible = Assert.Single(
                    await getAlerts.HandleAsync(new GetAlertsQuery(OnlyUnreviewed: true)),
                    a => a.AthleteId == athleteId);
                Assert.Equal("High", visible.Severity);
                Assert.Equal("LoadReduction", visible.SuggestedAdjustmentType);
                Assert.Equal(20, visible.SuggestedAdjustmentPercentage);
                Assert.Contains("partido próximo (07/11)", visible.Reason);
                Assert.Contains("20%", visible.Suggestion);

                var detail = await getAlertById.HandleAsync(new GetAlertByIdQuery(visible.Id));
                Assert.Equal(athleteId, detail.AthleteId);
            }

            // Otro PF no lo ve ni puede obtenerlo por id.
            using (currentUser.UsePfContext(Guid.NewGuid()))
            {
                Assert.DoesNotContain(
                    await getAlerts.HandleAsync(new GetAlertsQuery(OnlyUnreviewed: true)),
                    a => a.AthleteId == athleteId);
                await Assert.ThrowsAsync<NotFoundException>(() =>
                    getAlertById.HandleAsync(new GetAlertByIdQuery(alert.Id)));
            }
        });
    }

    [Fact]
    public async Task Given_AthleteWithMultipleCategories_When_MatchOnlyInSecondCategory_RealInfrastructure()
    {
        await RunInTestTenantAsync(async tenant =>
        {
            // DADO: atleta en dos categorías; solo la segunda tiene partido el sábado (D+3).
            using var pfClient = CreateClient(_settings.EstructuraUrl, tenant.PfId, "PreparadorFisico");
            var organizationId = await CreateOrganizationAsync(pfClient, "Club Multi K477");
            var firstCategoryId = await CreateCategoryAsync(pfClient, organizationId, "Categoria Principal");
            var secondCategoryId = await CreateCategoryAsync(pfClient, organizationId, "Categoria Secundaria");
            var athleteId = await CreateAthleteAsync(pfClient, "Angel", "Di Maria");
            tenant.AthleteIds.Add(athleteId);
            await AssignCategoryAsync(pfClient, athleteId, firstCategoryId);
            await AssignCategoryAsync(pfClient, athleteId, secondCategoryId);
            await CreateMatchEventAsync(pfClient, secondCategoryId, Saturday);

            await InsertCheckInAsync(athleteId, tenant.PfId, new DateOnly(2026, 11, 2), effortRate: 8);
            await InsertCheckInAsync(athleteId, tenant.PfId, new DateOnly(2026, 11, 3), effortRate: 9);

            var currentUser = new CurrentUser(new HttpContextAccessor());
            await using var context = CreateContext(currentUser);
            using var estructuraClient = CreateClient(_settings.EstructuraUrl, Guid.NewGuid(), "Servicio");
            using var seguimientoClient = CreateClient(_settings.SeguimientoUrl, Guid.NewGuid(), "Servicio");
            var handler = CreateHandler(
                estructuraClient, seguimientoClient, new PreventiveAlertRepository(context), context, currentUser);

            // CUANDO
            await handler.HandleAsync(new ProcessDailyOverloadAnalysisCommand(Wednesday));

            // ENTONCES: se detecta el partido de la segunda categoría y se aplica el umbral reducido.
            var alert = await context.PreventiveAlerts.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(a => a.AthleteId == athleteId);
            Assert.Equal(tenant.PfId, alert.PfId);
            Assert.Equal(AlertType.AcuteOverload, alert.Type);
            Assert.Equal(AlertSeverity.High, alert.Severity);
            Assert.Equal(20, alert.SuggestedAdjustmentPercentage);
            Assert.Contains("El atleta acumula 2 sesiones de esfuerzo alto y tiene un partido próximo", alert.Reason);
            Assert.Contains("07/11", alert.Reason);
        });
    }

    // =========================================================================
    // Ciclo de vida del tenant de prueba
    // =========================================================================

    private sealed class TestTenant(Guid pfId)
    {
        public Guid PfId { get; } = pfId;
        public List<Guid> AthleteIds { get; } = [];
    }

    /// <summary>
    /// Ejecuta la prueba con un PF nuevo y elimina en finally todo lo creado bajo ese PF
    /// (Estructura y Seguimiento). Si la prueba falla, el error original no queda oculto por la limpieza.
    /// </summary>
    private async Task RunInTestTenantAsync(Func<TestTenant, Task> body)
    {
        var tenant = new TestTenant(Guid.NewGuid());
        await EnsureTenantIsUnusedAsync(tenant.PfId);

        var bodyFailed = false;
        try
        {
            await body(tenant);
        }
        catch
        {
            bodyFailed = true;
            throw;
        }
        finally
        {
            try
            {
                await CleanupTenantAsync(tenant);
            }
            catch (Exception cleanupError) when (bodyFailed)
            {
                output.WriteLine($"No se pudo limpiar el PF de prueba {tenant.PfId}: {cleanupError.Message}");
            }
        }
    }

    // Garantía de que la limpieza por pf_id nunca toca datos preexistentes ni de la demo.
    private async Task EnsureTenantIsUnusedAsync(Guid pfId)
    {
        await using var connection = new NpgsqlConnection(_settings.EstructuraDbConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM organizations WHERE pf_id = @pfId", connection);
        command.Parameters.AddWithValue("pfId", pfId);
        var existing = (long)(await command.ExecuteScalarAsync())!;
        if (existing > 0)
            throw new InvalidOperationException($"El PF de prueba {pfId} ya tiene datos en Estructura.");
    }

    private async Task CleanupTenantAsync(TestTenant tenant)
    {
        if (tenant.PfId == Guid.Empty)
            throw new InvalidOperationException("No se puede limpiar un PF de prueba vacío.");

        // Seguimiento: check-ins insertados por la prueba.
        if (tenant.AthleteIds.Count > 0)
        {
            await using var seguimiento = new NpgsqlConnection(_settings.SeguimientoDbConnectionString);
            await seguimiento.OpenAsync();
            await using var deleteCheckIns = new NpgsqlCommand(
                "DELETE FROM check_ins WHERE pf_id = @pfId AND athlete_id = ANY(@athleteIds)", seguimiento);
            deleteCheckIns.Parameters.AddWithValue("pfId", tenant.PfId);
            deleteCheckIns.Parameters.AddWithValue("athleteIds", tenant.AthleteIds.ToArray());
            await deleteCheckIns.ExecuteNonQueryAsync();
        }

        // Estructura: los DELETE de la API son lógicos (deleted_at) y dejarían filas acumuladas,
        // por eso se borra físicamente, solo bajo el PF de la prueba y respetando las claves foráneas.
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
            delete.Parameters.AddWithValue("pfId", tenant.PfId);
            await delete.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ProcessDailyOverloadAnalysisCommandHandler CreateHandler(
        HttpClient estructuraClient,
        HttpClient seguimientoClient,
        PreventiveAlertRepository alertRepository,
        AppDbContext context,
        ICurrentUser currentUser) =>
        new(
            new StructureAthleteClient(estructuraClient, NullLogger<StructureAthleteClient>.Instance),
            new SeguimientoClient(seguimientoClient, NullLogger<SeguimientoClient>.Instance),
            new StructureCalendarClient(estructuraClient, NullLogger<StructureCalendarClient>.Instance),
            alertRepository,
            new UnitOfWork(context),
            currentUser,
            NullLogger<ProcessDailyOverloadAnalysisCommandHandler>.Instance);

    private AppDbContext CreateContext(ICurrentUser currentUser) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options, currentUser);

    private HttpClient CreateClient(Uri baseUrl, Guid subjectId, string role)
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
            new(JwtRegisteredClaimNames.Name, $"Prueba KAN-477 {subjectId:N}"),
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

    private static Task<Guid> CreateOrganizationAsync(HttpClient client, string name) =>
        PostAndReadIdAsync(client, "api/v1/organizations", new { name, description = "Datos de prueba KAN-477" });

    private static Task<Guid> CreateCategoryAsync(HttpClient client, Guid organizationId, string name) =>
        PostAndReadIdAsync(client, "api/v1/categories",
            new { organizationId, name, description = "Datos de prueba KAN-477" });

    private static Task<Guid> CreateAthleteAsync(HttpClient client, string firstName, string lastName) =>
        PostAndReadIdAsync(client, "api/v1/athletes", new
        {
            firstName,
            lastName,
            email = $"kan477.{Guid.NewGuid():N}@test.local",
            phone = "+5491122334455",
            isMinor = false,
            allowsPhysicalTraining = true,
            allowsHabitsRecommendations = true
        });

    private static async Task AssignCategoryAsync(HttpClient client, Guid athleteId, Guid categoryId)
    {
        using var response = await client.PostAsync($"api/v1/athletes/{athleteId}/categories/{categoryId}", null);
        response.EnsureSuccessStatusCode();
    }

    private static Task<Guid> CreateMatchEventAsync(HttpClient client, Guid categoryId, DateOnly date) =>
        PostAndReadIdAsync(client, $"api/v1/categories/{categoryId}/events", new
        {
            date = date.ToString("yyyy-MM-dd"),
            startTime = "16:00:00",
            opponent = "Rival FC",
            venue = CompetitiveEventVenueHome,
            type = CompetitiveEventTypeOfficial,
            notes = "Partido oficial KAN-477"
        });

    // Valores de los enums de Estructura (Home = 1, Official = 1); el contrato los recibe como enteros.
    private const int CompetitiveEventVenueHome = 1;
    private const int CompetitiveEventTypeOfficial = 1;

    // Seguimiento no expone un alta de check-in para el rol Servicio (el atleta lo envía con su propio token),
    // por eso la prueba inserta el check-in en la base de Seguimiento y lo lee luego por su API interna real.
    private async Task InsertCheckInAsync(Guid athleteId, Guid pfId, DateOnly date, int effortRate)
    {
        await using var connection = new NpgsqlConnection(_settings.SeguimientoDbConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO check_ins (
                id, athlete_id, date, traffic_light, sleep_hours, sleep_quality, muscle_soreness,
                fatigue_level, mood_rate, stress_level, is_sick, effort_rate, pf_id, created_at, has_active_injury)
            VALUES (
                @id, @athleteId, @date, 0, 8.0, 4, 2,
                2, 4, 2, false, @effortRate, @pfId, NOW(), false)
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("athleteId", athleteId);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("effortRate", effortRate);
        command.Parameters.AddWithValue("pfId", pfId);
        await command.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // Configuración del entorno de integración
    // =========================================================================

    /// <summary>
    /// Lee la configuración de variables de entorno y, si faltan, del .env local (no versionado)
    /// o del .env.example (valores de demostración). No contiene secretos en el código.
    /// </summary>
    private sealed record ServiceIntegrationSettings(
        Uri EstructuraUrl,
        Uri SeguimientoUrl,
        string EstructuraDbConnectionString,
        string SeguimientoDbConnectionString,
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
                    $"Falta la variable '{key}' para las pruebas de integración (definila o creá el .env desde .env.example).");

            var dbUser = Get("POSTGRES_USER");
            var dbPassword = Get("POSTGRES_PASSWORD");
            var dbHost = Get("PROTRAINING_TEST_DB_HOST", "localhost");
            string Db(string name, string port) =>
                $"Host={dbHost};Port={port};Database={name};Username={dbUser};Password={dbPassword}";

            return new ServiceIntegrationSettings(
                new Uri(Get("PROTRAINING_TEST_ESTRUCTURA_URL", "http://localhost:8082/")),
                new Uri(Get("PROTRAINING_TEST_SEGUIMIENTO_URL", "http://localhost:8084/")),
                Db("estructura", Get("PROTRAINING_TEST_ESTRUCTURA_DB_PORT", "5434")),
                Db("seguimiento", Get("PROTRAINING_TEST_SEGUIMIENTO_DB_PORT", "5436")),
                Get("JWT_KEY"),
                Get("JWT_ISSUER"),
                Get("JWT_AUDIENCE"));
        }

        public async Task EnsureServicesAvailableAsync()
        {
            var problems = new List<string>();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            foreach (var (name, url) in new[] { ("Estructura", EstructuraUrl), ("Seguimiento", SeguimientoUrl) })
            {
                try
                {
                    using var response = await http.GetAsync(new Uri(url, "health"));
                    if (!response.IsSuccessStatusCode)
                        problems.Add($"{name} respondió {(int)response.StatusCode} en {url}health");
                }
                catch (Exception ex)
                {
                    problems.Add($"{name} no responde en {url}health ({ex.Message})");
                }
            }

            foreach (var (name, connectionString) in new[]
                     {
                         ("db-estructura", EstructuraDbConnectionString),
                         ("db-seguimiento", SeguimientoDbConnectionString)
                     })
            {
                try
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                }
                catch (Exception ex)
                {
                    problems.Add($"{name} no acepta conexiones ({ex.Message})");
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Las pruebas de integración de KAN-477 necesitan Estructura y Seguimiento reales. " +
                    "Levantalos con 'docker compose up -d --wait estructura seguimiento'. Detalle: " +
                    string.Join("; ", problems));
            }
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
