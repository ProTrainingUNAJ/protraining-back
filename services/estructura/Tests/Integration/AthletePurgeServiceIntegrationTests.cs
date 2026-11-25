using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Athletes.PurgePendingAthletes;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Estructura.Tests.Integration;

/// <summary>
/// Prueba de integración real entre los 5 microservicios para la purga de un atleta (KAN-501).
/// No utiliza mocks de servicios externos: crea datos reales por HTTP en Identidad, Estructura,
/// Entrenamiento, Seguimiento y Asistente, da de baja al atleta, ejecuta la purga con retención 0
/// y verifica que no queden registros en ninguno de los 5 servicios.
/// Requiere: docker compose up -d --build --wait identidad estructura entrenamiento seguimiento asistente
/// </summary>
[Trait("Category", "ServiceIntegration")]
[Trait("Category", "RequiresDocker")]
public sealed class AthletePurgeServiceIntegrationTests : IAsyncLifetime
{
    private sealed class ServiceCurrentUser : ICurrentUser
    {
        public Guid? PfId => null;
        public Guid? UserId => null;
        public string? Role => "Servicio";
        public string? Email => null;
    }

    private readonly ServiceIntegrationSettings _settings = ServiceIntegrationSettings.Load();

    public async Task InitializeAsync()
    {
        await _settings.EnsureAllServicesAvailableAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Given_AthleteWithDataInAll5Services_When_SoftDeletedAndPurgedWithZeroRetention_Then_DisappearsFromAll5Services()
    {
        var pfId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..10];
        var athleteEmail = $"kan501.{uniqueSuffix}@protraining.test";
        const string athletePassword = "PasswordSegura123!";

        Guid? accountId = null;
        Guid? athleteId = null;

        try
        {
            using var pfEstructuraClient = CreatePfClient(_settings.EstructuraUrl, pfId);
            using var pfEntrenamientoClient = CreatePfClient(_settings.EntrenamientoUrl, pfId);
            using var pfSeguimientoClient = CreatePfClient(_settings.SeguimientoUrl, pfId);
            using var pfAsistenteClient = CreatePfClient(_settings.AsistenteUrl, pfId);
            using var identidadAnonymousClient = new HttpClient { BaseAddress = _settings.IdentidadUrl };

            // 1. Estructura: crear organización, categoría y token de invitación para el atleta
            var orgId = await PostAndReadIdAsync(pfEstructuraClient, "api/v1/organizations", new
            {
                name = $"Club KAN-501 {uniqueSuffix}",
                description = "Organización de prueba E2E de purga"
            });

            var categoryId = await PostAndReadIdAsync(pfEstructuraClient, "api/v1/categories", new
            {
                organizationId = orgId,
                name = $"Plantel KAN-501 {uniqueSuffix}",
                description = "Categoría de prueba E2E de purga"
            });

            var invitationJson = await PostAndReadJsonAsync(
                pfEstructuraClient,
                $"api/v1/categories/{categoryId}/invitation-token",
                new { });
            var invitationToken = invitationJson["token"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(invitationToken));

            // 2. Identidad: registrar la cuenta real del atleta usando el token de invitación
            var registerJson = await PostAndReadJsonAsync(
                identidadAnonymousClient,
                "api/v1/auth/register-by-invitation",
                new
                {
                    email = athleteEmail,
                    password = athletePassword,
                    fullName = "Atleta Purga E2E",
                    token = invitationToken,
                    deviceId = $"e2e-device-{uniqueSuffix}"
                });

            var athleteAccessToken = registerJson["accessToken"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(athleteAccessToken));

            accountId = registerJson["user"]?["id"]?.GetValue<Guid>();
            Assert.True(accountId.HasValue && accountId.Value != Guid.Empty);

            Assert.Equal(1, await CountRowsAsync(
                _settings.IdentidadDbConnectionString,
                "SELECT COUNT(*) FROM db_identity.users WHERE \"Id\" = @id",
                accountId.Value));
            Assert.True(await CountRowsAsync(
                _settings.IdentidadDbConnectionString,
                "SELECT COUNT(*) FROM db_identity.refresh_tokens WHERE user_id = @id",
                accountId.Value) >= 1);

            // 3. Estructura: aceptar la invitación con el token real del atleta -> crea ficha vinculada a AccountId
            using var athleteEstructuraClient = CreateClientWithBearer(_settings.EstructuraUrl, athleteAccessToken!);
            using var athleteSeguimientoClient = CreateClientWithBearer(_settings.SeguimientoUrl, athleteAccessToken!);

            var acceptedAthleteJson = await PostAndReadJsonAsync(
                athleteEstructuraClient,
                $"api/v1/invitations/{invitationToken}/accept",
                new
                {
                    firstName = "Atleta",
                    lastName = "Purga E2E",
                    phone = "+5491155551234",
                    birthDate = "2000-05-15"
                });

            athleteId = acceptedAthleteJson["id"]?.GetValue<Guid>();
            Assert.True(athleteId.HasValue && athleteId.Value != Guid.Empty);
            Assert.NotEqual(accountId.Value, athleteId.Value);

            // 4. Seguimiento: cargar un check-in real del atleta
            await PostAndReadJsonAsync(
                athleteSeguimientoClient,
                $"api/v1/athletes/{athleteId.Value}/check-in",
                new
                {
                    sleepHours = 8.0,
                    sleepQuality = 4,
                    muscleSoreness = 2,
                    fatigueLevel = 2,
                    moodRate = 4,
                    stressLevel = 2
                });

            Assert.Equal(1, await CountRowsAsync(
                _settings.SeguimientoDbConnectionString,
                "SELECT COUNT(*) FROM check_ins WHERE athlete_id = @id",
                athleteId.Value));

            // 5. Entrenamiento: obtener un ejercicio del catálogo y cargar un récord 1RM real
            var exerciseId = await GetFirstExerciseIdAsync(pfEntrenamientoClient);
            await PostAndReadJsonAsync(
                pfEntrenamientoClient,
                $"api/v1/athletes/{athleteId.Value}/records",
                new
                {
                    exerciseId,
                    weightOneRepMaxKg = 110.0,
                    origin = "TestedInSession"
                });

            Assert.Equal(1, await CountRowsAsync(
                _settings.EntrenamientoDbConnectionString,
                "SELECT COUNT(*) FROM athlete_records WHERE athlete_id = @id",
                athleteId.Value));

            // 6. Asistente: cargar un aviso preventivo real para el atleta
            await PostAndReadJsonAsync(
                pfAsistenteClient,
                "api/v1/alerts",
                new
                {
                    athleteId = athleteId.Value,
                    athleteName = "Atleta Purga E2E",
                    reason = "Prueba E2E de purga en los 5 servicios",
                    suggestion = "Reducir carga en la sesión de prueba",
                    severity = "Low"
                });

            Assert.Equal(1, await CountRowsAsync(
                _settings.AsistenteDbConnectionString,
                "SELECT COUNT(*) FROM preventive_alerts WHERE athlete_id = @id",
                athleteId.Value));

            // 7. Estructura: dar de baja lógica al atleta (genera PendingPurge con AccountId)
            using (var deleteResponse = await pfEstructuraClient.DeleteAsync($"api/v1/athletes/{athleteId.Value}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
            }

            using (var getDeletedResponse = await pfEstructuraClient.GetAsync($"api/v1/athletes/{athleteId.Value}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
            }

            Assert.Equal(1, await CountRowsAsync(
                _settings.EstructuraDbConnectionString,
                "SELECT COUNT(*) FROM pending_purges WHERE athlete_id = @id AND status = 'Pending' AND account_id = @accountId",
                athleteId.Value,
                ("accountId", accountId.Value)));

            // 8. Ejecutar el job real de purga con retención 0 contra los 5 servicios reales (sin mocks)
            var purgeResult = await ExecuteRealPurgeJobAsync();
            Assert.True(purgeResult.PurgedSuccessfully >= 1);
            Assert.Equal(0, purgeResult.FailedOrPostponed);

            // 9. Verificar en CADA UNO DE LOS 5 SERVICIOS que no quedó ningún dato del atleta

            // 9.a) Identidad: login rechazado y 0 filas en users, refresh_tokens, user_roles y user_photos
            using (var loginAfterPurge = await identidadAnonymousClient.PostAsJsonAsync("api/v1/auth/login", new
            {
                email = athleteEmail,
                password = athletePassword,
                deviceId = $"e2e-check-{uniqueSuffix}"
            }))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, loginAfterPurge.StatusCode);
            }

            Assert.Equal(0, await CountRowsAsync(
                _settings.IdentidadDbConnectionString,
                "SELECT COUNT(*) FROM db_identity.users WHERE \"Id\" = @id",
                accountId.Value));
            Assert.Equal(0, await CountRowsAsync(
                _settings.IdentidadDbConnectionString,
                "SELECT COUNT(*) FROM db_identity.refresh_tokens WHERE user_id = @id",
                accountId.Value));
            Assert.Equal(0, await CountRowsAsync(
                _settings.IdentidadDbConnectionString,
                "SELECT COUNT(*) FROM db_identity.user_roles WHERE \"UserId\" = @id",
                accountId.Value));
            Assert.Equal(0, await CountRowsAsync(
                _settings.IdentidadDbConnectionString,
                "SELECT COUNT(*) FROM db_identity.user_photos WHERE user_id = @id",
                accountId.Value));

            // 9.b) Estructura: 404 por API, 0 filas en athletes/athlete_categories y purga en estado Processed
            using (var finalGetAthlete = await pfEstructuraClient.GetAsync($"api/v1/athletes/{athleteId.Value}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, finalGetAthlete.StatusCode);
            }

            Assert.Equal(0, await CountRowsAsync(
                _settings.EstructuraDbConnectionString,
                "SELECT COUNT(*) FROM athletes WHERE id = @id",
                athleteId.Value));
            Assert.Equal(0, await CountRowsAsync(
                _settings.EstructuraDbConnectionString,
                "SELECT COUNT(*) FROM athlete_categories WHERE athlete_id = @id",
                athleteId.Value));
            Assert.Equal(1, await CountRowsAsync(
                _settings.EstructuraDbConnectionString,
                "SELECT COUNT(*) FROM pending_purges WHERE athlete_id = @id AND status = 'Processed' AND processed_at IS NOT NULL",
                athleteId.Value));

            // 9.c) Entrenamiento: 404 por API (el atleta ya no existe) y 0 filas en athlete_records
            using (var recordsResponse = await pfEntrenamientoClient.GetAsync($"api/v1/athletes/{athleteId.Value}/records"))
            {
                Assert.Equal(HttpStatusCode.NotFound, recordsResponse.StatusCode);
            }

            Assert.Equal(0, await CountRowsAsync(
                _settings.EntrenamientoDbConnectionString,
                "SELECT COUNT(*) FROM athlete_records WHERE athlete_id = @id",
                athleteId.Value));

            // 9.d) Seguimiento: sin check-ins por API (lista vacía o 404) y 0 filas en check_ins
            using (var checkInsResponse = await pfSeguimientoClient.GetAsync($"api/v1/athletes/{athleteId.Value}/check-ins"))
            {
                if (checkInsResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    checkInsResponse.EnsureSuccessStatusCode();
                    var checkInsJson = await checkInsResponse.Content.ReadFromJsonAsync<JsonNode>();
                    Assert.NotNull(checkInsJson);
                    var items = checkInsJson is JsonArray arr ? arr : checkInsJson["items"]?.AsArray();
                    Assert.NotNull(items);
                    Assert.Empty(items);
                }
            }

            Assert.Equal(0, await CountRowsAsync(
                _settings.SeguimientoDbConnectionString,
                "SELECT COUNT(*) FROM check_ins WHERE athlete_id = @id",
                athleteId.Value));

            // 9.e) Asistente: sin alertas por API para el atleta y 0 filas en preventive_alerts
            using (var alertsResponse = await pfAsistenteClient.GetAsync("api/v1/alerts?onlyUnreviewed=false"))
            {
                alertsResponse.EnsureSuccessStatusCode();
                var alertsJson = await alertsResponse.Content.ReadFromJsonAsync<JsonNode>();
                Assert.NotNull(alertsJson);
                var alerts = alertsJson is JsonArray arr ? arr : alertsJson["items"]?.AsArray();
                Assert.NotNull(alerts);
                Assert.DoesNotContain(alerts, item =>
                    item?["athleteId"]?.GetValue<Guid>() == athleteId.Value);
            }

            Assert.Equal(0, await CountRowsAsync(
                _settings.AsistenteDbConnectionString,
                "SELECT COUNT(*) FROM preventive_alerts WHERE athlete_id = @id",
                athleteId.Value));
        }
        finally
        {
            await CleanupTestDataAsync(pfId, athleteId, accountId);
        }
    }

    private async Task<ProcessPendingPurgesResult> ExecuteRealPurgeJobAsync()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_settings.EstructuraDbConnectionString)
            .Options;

        await using var dbContext = new AppDbContext(dbOptions, new ServiceCurrentUser());

        var tokenConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = _settings.JwtKey,
                ["Jwt:Issuer"] = _settings.JwtIssuer,
                ["Jwt:Audience"] = _settings.JwtAudience
            })
            .Build();
        var serviceTokenFactory = new ServiceTokenFactory(tokenConfig);

        using var serviceHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        serviceHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", serviceTokenFactory.CreateToken());

        var purgeClient = new ExternalServicePurgeClient(
            serviceHttpClient,
            NullLogger<ExternalServicePurgeClient>.Instance);

        var dispatcherConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:Identidad:BaseUrl"] = _settings.IdentidadUrl.ToString().TrimEnd('/'),
                ["Services:Entrenamiento:BaseUrl"] = _settings.EntrenamientoUrl.ToString().TrimEnd('/'),
                ["Services:Seguimiento:BaseUrl"] = _settings.SeguimientoUrl.ToString().TrimEnd('/'),
                ["Services:Asistente:BaseUrl"] = _settings.AsistenteUrl.ToString().TrimEnd('/')
            })
            .Build();

        var dispatcher = new AthletePurgeDispatcher(
            purgeClient,
            dispatcherConfig,
            NullLogger<AthletePurgeDispatcher>.Instance);

        var handler = new ProcessPendingPurgesCommandHandler(
            new PendingPurgeRepository(dbContext),
            dispatcher,
            new EstructuraPurgeRepository(dbContext),
            new UnitOfWork(dbContext),
            TimeProvider.System);

        return await handler.HandleAsync(new ProcessPendingPurgesCommand(RetentionDays: 0), CancellationToken.None);
    }

    private HttpClient CreatePfClient(Uri baseUrl, Guid pfId) =>
        CreateClientWithBearer(baseUrl, CreatePfToken(pfId));

    private static HttpClient CreateClientWithBearer(Uri baseUrl, string token)
    {
        var client = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string CreatePfToken(Guid pfId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, pfId.ToString()),
            new(JwtRegisteredClaimNames.Name, $"Test KAN-501 {pfId:N}"),
            new("role", "PreparadorFisico"),
            new("pf_id", pfId.ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.JwtKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _settings.JwtIssuer,
            _settings.JwtAudience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<Guid> PostAndReadIdAsync(HttpClient client, string url, object body)
    {
        var json = await PostAndReadJsonAsync(client, url, body);
        var id = json["id"]?.GetValue<Guid>();
        if (!id.HasValue || id.Value == Guid.Empty)
        {
            throw new InvalidOperationException($"POST {url} no devolvió un campo 'id' válido: {json}");
        }

        return id.Value;
    }

    private static async Task<JsonObject> PostAndReadJsonAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{client.BaseAddress}{url} respondió {(int)response.StatusCode}: {content}");
        }

        return JsonNode.Parse(content)?.AsObject()
            ?? throw new InvalidOperationException($"Respuesta JSON inválida de POST {url}: {content}");
    }

    private static async Task<Guid> GetFirstExerciseIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/v1/exercises?pageSize=1");
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Entrenamiento respondió {(int)response.StatusCode} en GET api/v1/exercises: {content}");
        }

        var root = JsonNode.Parse(content)
            ?? throw new InvalidOperationException($"Respuesta JSON inválida de GET api/v1/exercises: {content}");

        JsonArray? items = root is JsonArray array
            ? array
            : root["items"]?.AsArray();

        if (items is null || items.Count == 0)
        {
            throw new InvalidOperationException("El catálogo de ejercicios de Entrenamiento está vacío.");
        }

        return items[0]!["id"]!.GetValue<Guid>();
    }

    private static async Task<long> CountRowsAsync(
        string connectionString,
        string sql,
        Guid id,
        params (string Name, object Value)[] extraParams)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        foreach (var (name, value) in extraParams)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var scalar = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(scalar);
    }

    private async Task CleanupTestDataAsync(Guid pfId, Guid? athleteId, Guid? accountId)
    {
        // Limpieza defensiva en Estructura (organización, categoría, invitaciones y purga procesada del PF de prueba)
        try
        {
            string[] estructuraTables =
            [
                "competitive_events", "season_phases", "category_invitations", "athlete_categories",
                "medical_record_audit_logs", "medical_records", "digital_consents", "athlete_photos",
                "push_subscriptions", "pending_purges", "athletes", "categories",
                "organization_photos", "organizations"
            ];

            await using var conn = new NpgsqlConnection(_settings.EstructuraDbConnectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            foreach (var table in estructuraTables)
            {
                await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE pf_id = @pfId", conn, tx);
                cmd.Parameters.AddWithValue("pfId", pfId);
                await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch
        {
            // No ocultar el error principal del test si la limpieza falla
        }

        // Si el test falló antes de completar la purga, limpiar también posibles filas residuales en los demás servicios
        if (accountId.HasValue)
        {
            await TryExecuteDeleteAsync(
                _settings.IdentidadDbConnectionString,
                "DELETE FROM db_identity.user_photos WHERE user_id = @id; DELETE FROM db_identity.refresh_tokens WHERE user_id = @id; DELETE FROM db_identity.user_roles WHERE \"UserId\" = @id; DELETE FROM db_identity.users WHERE \"Id\" = @id;",
                accountId.Value);
        }

        if (athleteId.HasValue)
        {
            await TryExecuteDeleteAsync(
                _settings.EntrenamientoDbConnectionString,
                "DELETE FROM athlete_records WHERE athlete_id = @id;",
                athleteId.Value);
            await TryExecuteDeleteAsync(
                _settings.SeguimientoDbConnectionString,
                "DELETE FROM check_ins WHERE athlete_id = @id;",
                athleteId.Value);
            await TryExecuteDeleteAsync(
                _settings.AsistenteDbConnectionString,
                "DELETE FROM preventive_alerts WHERE athlete_id = @id;",
                athleteId.Value);
        }
    }

    private static async Task TryExecuteDeleteAsync(string connectionString, string sql, Guid id)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // No ocultar el error principal del test
        }
    }

    private sealed record ServiceIntegrationSettings(
        Uri IdentidadUrl,
        Uri EstructuraUrl,
        Uri EntrenamientoUrl,
        Uri SeguimientoUrl,
        Uri AsistenteUrl,
        string IdentidadDbConnectionString,
        string EstructuraDbConnectionString,
        string EntrenamientoDbConnectionString,
        string SeguimientoDbConnectionString,
        string AsistenteDbConnectionString,
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
                    $"Falta la variable '{key}' para las pruebas de integración entre microservicios.");

            var dbUser = Get("POSTGRES_USER");
            var dbPassword = Get("POSTGRES_PASSWORD");
            var dbHost = Get("PROTRAINING_TEST_DB_HOST", "localhost");

            string BuildConn(string portEnvVar, string defaultPort, string database)
            {
                var port = Get(portEnvVar, defaultPort);
                return $"Host={dbHost};Port={port};Database={database};Username={dbUser};Password={dbPassword}";
            }

            return new ServiceIntegrationSettings(
                new Uri(Get("PROTRAINING_TEST_IDENTIDAD_URL", "http://localhost:8081/")),
                new Uri(Get("PROTRAINING_TEST_ESTRUCTURA_URL", "http://localhost:8082/")),
                new Uri(Get("PROTRAINING_TEST_ENTRENAMIENTO_URL", "http://localhost:8083/")),
                new Uri(Get("PROTRAINING_TEST_SEGUIMIENTO_URL", "http://localhost:8084/")),
                new Uri(Get("PROTRAINING_TEST_ASISTENTE_URL", "http://localhost:8085/")),
                BuildConn("PROTRAINING_TEST_IDENTIDAD_DB_PORT", "5433", "identidad"),
                BuildConn("PROTRAINING_TEST_ESTRUCTURA_DB_PORT", "5434", "estructura"),
                BuildConn("PROTRAINING_TEST_ENTRENAMIENTO_DB_PORT", "5435", "entrenamiento"),
                BuildConn("PROTRAINING_TEST_SEGUIMIENTO_DB_PORT", "5436", "seguimiento"),
                BuildConn("PROTRAINING_TEST_ASISTENTE_DB_PORT", "5437", "asistente"),
                Get("JWT_KEY"),
                Get("JWT_ISSUER"),
                Get("JWT_AUDIENCE"));
        }

        public async Task EnsureAllServicesAvailableAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            (string Name, Uri Url)[] services =
            [
                ("Identidad", IdentidadUrl),
                ("Estructura", EstructuraUrl),
                ("Entrenamiento", EntrenamientoUrl),
                ("Seguimiento", SeguimientoUrl),
                ("Asistente", AsistenteUrl)
            ];

            foreach (var (name, url) in services)
            {
                using var response = await http.GetAsync(new Uri(url, "health"));
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"El servicio {name} ({url}health) respondió {(int)response.StatusCode}. " +
                        "Levantá los servicios con: docker compose up -d --build --wait identidad estructura entrenamiento seguimiento asistente");
                }
            }

            await using var connection = new NpgsqlConnection(EstructuraDbConnectionString);
            await connection.OpenAsync();
        }

        private static Dictionary<string, string> LoadEnvFile()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                directory = directory.Parent;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (directory is null)
            {
                return values;
            }

            var envFile = Path.Combine(directory.FullName, ".env");
            if (!File.Exists(envFile))
            {
                envFile = Path.Combine(directory.FullName, ".env.example");
            }

            if (!File.Exists(envFile))
            {
                return values;
            }

            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator > 0)
                {
                    values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
                }
            }

            return values;
        }
    }
}
