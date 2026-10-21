using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Infrastructure.Repositories;
using Infrastructure.Services;
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
public sealed class SeguimientoTestApplicationFactory : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TestServer _server;

    private SeguimientoTestApplicationFactory(SqliteConnection sqliteConnection, TestServer server)
    {
        _sqliteConnection = sqliteConnection;
        _server = server;
    }

    // Levanta el host con el mismo DI de infraestructura que el servicio y ejecuta
    // el arranque: crea el esquema (EnsureCreated en vez de migraciones, que son de
    // Npgsql) y siembra el catálogo, tal como hace ApplyMigrationsAsync en producción.
    public static async Task<SeguimientoTestApplicationFactory> CreateAsync(Guid? pfId = null)
    {
        var sqliteConnection = new SqliteConnection("DataSource=:memory:");
        sqliteConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

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
            })
            .Configure(_ => { });

        var server = new TestServer(builder);

        using (var scope = server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.EnsureCreatedAsync();

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
            await CatalogTestSeeder.SeedAsync(context, logger);
        }

        return new SeguimientoTestApplicationFactory(sqliteConnection, server);
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
