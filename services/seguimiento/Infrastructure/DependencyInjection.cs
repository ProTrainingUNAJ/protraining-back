using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string Required(string key) =>
            !string.IsNullOrWhiteSpace(configuration[key])
                ? configuration[key]!
                : throw new InvalidOperationException(
                    $"Falta la variable de configuración {key.Replace(":", "__")}.");

        var user = Required("Database:User");
        if (user.Equals("root", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La API requiere un usuario de aplicación, no root.");

        if (!uint.TryParse(Required("Database:Port"), out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Database__Port debe ser un puerto entre 1 y 65535.");

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = Required("Database:Host"),
            Port = (int)port,
            Database = Required("Database:Name"),
            Username = user,
            Password = Required("Database:Password"),
            Timeout = 3,
            CommandTimeout = 3
        }.ConnectionString;

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(3)));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();

        return services;
    }

    // Aplica las migraciones pendientes y siembra el catálogo al arrancar.
    // Sin migraciones registradas en el ensamblado no hace nada: protege el arranque
    // si el pipeline de migraciones se reordena o el servicio aún no tiene migraciones.
    public static async Task ApplyMigrationsAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!context.Database.GetMigrations().Any())
            return;

        await context.Database.MigrateAsync();

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        await CatalogTestSeeder.SeedAsync(context, logger);
    }
}
