using System.Net.Http.Headers;
using API.Controllers.Categories;
using API.Extensions;
using API.Middleware;
using API.Services;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.TrafficLight.GetCategoryTrafficLightToday;
using Domain.Services;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Seguimiento.Tests.Infrastructure;

// Factory de integración de Seguimiento, siguiendo el patrón de
// EstructuraTestApplicationFactory: TestServer con SQLite en memoria en lugar de
// PostgreSQL, para probar el pipeline real de arranque (esquema + seeder) sin Docker.
// KAN-367: además del pipeline de datos, levanta el endpoint real del semáforo con
// autenticación JWT y un Estructura falso controlable por test (caída, timeout, breaker).
public sealed class SeguimientoTestApplicationFactory : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TestServer _server;

    // Estructura falsa: cada test define qué le pasa al llamado del semáforo.
    public FakeStructureHandler Structure { get; }

    private SeguimientoTestApplicationFactory(
        SqliteConnection sqliteConnection, TestServer server, FakeStructureHandler structure)
    {
        _sqliteConnection = sqliteConnection;
        _server = server;
        Structure = structure;
    }

    // Levanta el host con el mismo DI de infraestructura que el servicio y ejecuta
    // el arranque: crea el esquema (EnsureCreated en vez de migraciones, que son de
    // Npgsql) y siembra el catálogo, tal como hace ApplyMigrationsAsync en producción.
    public static async Task<SeguimientoTestApplicationFactory> CreateAsync(Guid? pfId = null)
    {
        var sqliteConnection = new SqliteConnection("DataSource=:memory:");
        sqliteConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // JWT de prueba: mismas claves que exige AddJwtAuthentication (KAN-367).
                ["Jwt:Key"] = TestJwtGenerator.DefaultKey,
                ["Jwt:Issuer"] = TestJwtGenerator.DefaultIssuer,
                ["Jwt:Audience"] = TestJwtGenerator.DefaultAudience,
            })
            .Build();

        var structureHandler = new FakeStructureHandler();

        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseConfiguration(configuration)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);

                services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqliteConnection));

                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<ICheckInRepository, CheckInRepository>();
                services.AddScoped<ICatalogTestRepository, CatalogTestRepository>();
                services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUser>(_ => new TestCurrentUser(pfId));

                // Semáforo del día (KAN-367): motor y caso de uso registrados como en Program.cs.
                services.AddScoped<TrafficLightEngine>();
                services.AddScoped<GetCategoryTrafficLightTodayQueryHandler>();

                // Estructura por HTTP hacia el handler falso: misma cadena de producción
                // (StructureClient + reenvío del token) y control total del comportamiento por test.
                services.AddTransient<ForwardAuthorizationHandler>();
                services.AddHttpClient<IStructureClient, StructureClient>(client =>
                        client.BaseAddress = new Uri("http://estructura.test/"))
                    .ConfigurePrimaryHttpMessageHandler(() => structureHandler)
                    .AddHttpMessageHandler<ForwardAuthorizationHandler>();

                // Autenticación y controllers reales: el endpoint exige rol PreparadorFisico.
                services.AddJwtAuthentication(configuration);
                services.AddControllers()
                    .AddApplicationPart(typeof(CategoriesTrafficLightController).Assembly);
            })
            .Configure(app =>
            {
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseMiddleware<ExceptionMiddleware>();

                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        var server = new TestServer(builder);

        using (var scope = server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.EnsureCreatedAsync();

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
            await CatalogTestSeeder.SeedAsync(context, logger);
        }

        return new SeguimientoTestApplicationFactory(sqliteConnection, server, structureHandler);
    }

    // Cliente HTTP sin token: sirve para probar los 401/403 del endpoint.
    public HttpClient CreateClient() => _server.CreateClient();

    // Cliente con JWT de PF, igual que el que usa el front del preparador físico.
    public HttpClient CreateAuthenticatedClient(Guid pfId, string role = "PreparadorFisico")
    {
        var client = _server.CreateClient();
        var token = TestJwtGenerator.GenerateToken(pfId, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Ejecuta una consulta contra la base del factory con un scope propio y limpio.
    public async Task<T> ExecuteInDbContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(context);
    }

    // Variante para acciones que no devuelven resultado (por ejemplo volver a sembrar).
    public async Task ExecuteInDbContextAsync(Func<AppDbContext, Task> action)
    {
        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(context);
    }

    public async ValueTask DisposeAsync()
    {
        _server.Dispose();
        await _sqliteConnection.DisposeAsync();
    }

    public void Dispose()
    {
        _server.Dispose();
        _sqliteConnection.Dispose();
    }
}
