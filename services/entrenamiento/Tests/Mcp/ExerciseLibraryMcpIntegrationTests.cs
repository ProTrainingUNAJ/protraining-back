using System.Net.Http.Headers;
using System.Text.Json;
using API.Mcp;
using Application.DTOs.Mcp;
using Application.Interfaces.Persistence;
using Application.UseCases.Exercise.GetSubstitutesByZone;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Entrenamiento.Tests.Mcp;

public sealed class ExerciseLibraryMcpIntegrationTests
{
    [Fact]
    public async Task Given_McpClient_When_HandshakeAndCallObtenerSustitutosForRodilla_Then_ReturnsSubstitutesWithoutDeserializationErrors()
    {
        await using var factory = new EntrenamientoMcpWebApplicationFactory();
        var httpClient = factory.CreateClient();

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
            Name = "entrenamiento-mcp-test"
        }, httpClient);

        await using var mcpClient = await McpClient.CreateAsync(transport);

        // Apretón de manos (handshake): el servidor expone las tools registradas.
        var tools = await mcpClient.ListToolsAsync();
        Assert.Contains("ObtenerSustitutos", tools.Select(t => t.Name));

        // Llamada directa al proveedor (handler/tool logic) para contrastar ejecución.
        var restriction = BuildRodillaRestriction();
        var directHandler = new GetSubstitutesByZoneQueryHandler(new FakeRestrictionRepository([restriction]));
        var directResult = await directHandler.Handle(new GetSubstitutesByZoneQuery("rodilla"));
        Assert.NotEmpty(directResult.Substitutes);

        // Ejecución de la tool vía MCP (JSON-RPC) contra el servidor.
        var callResult = await mcpClient.CallToolAsync(
            "ObtenerSustitutos",
            new Dictionary<string, object?> { ["zona"] = "rodilla" });

        Assert.True(callResult.IsError != true);

        ZoneSubstitutesResultDto? response = null;
        if (callResult.StructuredContent is { } structured)
        {
            var structuredJson = JsonSerializer.Serialize(structured, JsonOptions);
            response = JsonSerializer.Deserialize<ZoneSubstitutesResultDto>(structuredJson, JsonOptions);
        }
        else
        {
            var contentJson = JsonSerializer.Serialize(callResult.Content, JsonOptions);
            response = JsonSerializer.Deserialize<ZoneSubstitutesResultDto>(contentJson, JsonOptions);
        }

        Assert.NotNull(response);
        Assert.Equal("rodilla", response!.Zone);
        Assert.NotEmpty(response.Substitutes);
        var directNames = directResult.Substitutes.Select(s => s.Name).ToHashSet();
        Assert.Contains(response.Substitutes, s => directNames.Contains(s.Name));
        Assert.All(response.Substitutes, s =>
        {
            Assert.NotEqual(Guid.Empty, s.ExerciseId);
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
        });
    }

    [Fact]
    public async Task Given_McpClient_When_CallsObtenerSustitutosForKneeAlias_Then_ReturnsNormalizedAliasResponse()
    {
        await using var factory = new EntrenamientoMcpWebApplicationFactory();
        var httpClient = factory.CreateClient();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
            Name = "entrenamiento-mcp-knee-alias-test"
        }, httpClient);

        await using var mcpClient = await McpClient.CreateAsync(transport);

        var callResult = await mcpClient.CallToolAsync(
            "ObtenerSustitutos",
            new Dictionary<string, object?> { ["zona"] = "knee" });

        Assert.True(callResult.IsError != true);
        var structuredJson = JsonSerializer.Serialize(callResult.StructuredContent, JsonOptions);
        var response = JsonSerializer.Deserialize<ZoneSubstitutesResultDto>(structuredJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("knee", response!.Zone);
        Assert.NotEmpty(response.Substitutes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ExerciseRestriction BuildRodillaRestriction()
    {
        var restrictedExercise = Exercise.Create("Sentadilla con barra", "Piernas");
        var substitute = Exercise.Create("Prensa de piernas", "Piernas");
        var restriction = ExerciseRestriction.Create("cuadriceps", restrictedExercise.Id, "Molestia en rodilla", minPainLevel: 1);
        // Se inyecta el sustituto con su ejercicio cargado para simular Include del repositorio.
        var sub = ExerciseSubstitute.Create(restriction.Id, substitute.Id, 1);
        typeof(ExerciseSubstitute).GetProperty(nameof(ExerciseSubstitute.SubstituteExercise))!
            .SetValue(sub, substitute);
        typeof(ExerciseRestriction).GetField("_substitutes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(restriction, new List<ExerciseSubstitute> { sub });
        return restriction;
    }

    private sealed class FakeRestrictionRepository(IReadOnlyList<ExerciseRestriction> restrictions) : IExerciseRestrictionRepository
    {
        public Task<List<ExerciseRestriction>> GetRestrictionsByZonesAndExercisesAsync(IEnumerable<string> zones, IEnumerable<Guid> exerciseIds, CancellationToken cancellationToken = default)
            => Task.FromResult(restrictions.Where(r => zones.Contains(r.Zone, StringComparer.OrdinalIgnoreCase) && exerciseIds.Contains(r.ExerciseId)).ToList());

        public Task<ExerciseRestriction?> GetRestrictionByExerciseAndZoneAsync(Guid exerciseId, string zone, CancellationToken cancellationToken = default)
            => Task.FromResult(restrictions.FirstOrDefault(r => r.ExerciseId == exerciseId && r.Zone.Equals(zone, StringComparison.OrdinalIgnoreCase)));

        public Task<List<ExerciseRestriction>> GetRestrictionsByZonesAsync(IEnumerable<string> zones, CancellationToken cancellationToken = default)
        {
            var set = zones.Select(z => z.Trim().ToLowerInvariant()).ToHashSet();
            return Task.FromResult(restrictions.Where(r => set.Contains(r.Zone)).ToList());
        }
    }

    private sealed class EntrenamientoMcpWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:User", "test");
            builder.UseSetting("Database:Password", "test");
            builder.UseSetting("Database:Name", "test");
            builder.UseSetting("Database:Host", "localhost");
            builder.UseSetting("Database:Port", "5432");
            builder.UseSetting("Database__User", "test");
            builder.UseSetting("Database__Password", "test");
            builder.UseSetting("Database__Name", "test");
            builder.UseSetting("Database__Host", "localhost");
            builder.UseSetting("Database__Port", "5432");
            builder.UseSetting("Services:Estructura:BaseUrl", "http://localhost:8081");
            builder.UseSetting("Jwt:Key", "test-key-test-key-test-key-test-key-123456");
            builder.UseSetting("Jwt:Issuer", "protraining-test");
            builder.UseSetting("Jwt:Audience", "protraining-test");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:User"] = "test",
                    ["Database:Password"] = "test",
                    ["Database:Name"] = "test",
                    ["Database:Host"] = "localhost",
                    ["Database:Port"] = "5432",
                    ["Database__User"] = "test",
                    ["Database__Password"] = "test",
                    ["Database__Name"] = "test",
                    ["Database__Host"] = "localhost",
                    ["Database__Port"] = "5432",
                    ["Services:Estructura:BaseUrl"] = "http://localhost:8081",
                    ["Jwt:Key"] = "test-key-test-key-test-key-test-key-123456",
                    ["Jwt:Issuer"] = "protraining-test",
                    ["Jwt:Audience"] = "protraining-test"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // Autenticación de prueba para cumplir la política de /mcp sin depender de JWT/infra externa.
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                // DB en memoria para evitar dependencias de PostgreSQL en la integración MCP.
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase($"mcp-tests-{Guid.NewGuid()}"));

                // Repositorio controlado con datos para "rodilla" (cuadriceps).
                services.RemoveAll<IExerciseRestrictionRepository>();
                services.AddScoped<IExerciseRestrictionRepository>(_ => new FakeRestrictionRepository([BuildRodillaRestriction()]));
            });
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Servicio")
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
