using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Infrastructure.Data;
using Infrastructure.Data.Seeders;
using Infrastructure.Repositories;
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
        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<IExerciseRestrictionRepository, ExerciseRestrictionRepository>();
        services.AddScoped<IAthleteRecordRepository, AthleteRecordRepository>();
        services.AddScoped<ITrainingPlanRepository, TrainingPlanRepository>();
        services.AddScoped<ITrainingSessionRepository, TrainingSessionRepository>();
        services.AddScoped<IPerformedSetRepository, PerformedSetRepository>();
        services.AddScoped<IRecordProposalRepository, RecordProposalRepository>();
        services.AddScoped<IExerciseVideoRepository, ExerciseVideoRepository>();
        services.AddScoped<IAthleteSessionAdjustmentRepository, AthleteSessionAdjustmentRepository>();

        return services;
    }

    // Applies pending migrations on startup. Does nothing if the service has no migrations yet.
    public static async Task ApplyMigrationsAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        if (context.Database.IsRelational())
        {
            if (context.Database.GetMigrations().Any())
                await context.Database.MigrateAsync();

            var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
            await ExerciseCatalogSeeder.SeedAsync(context, dataDirectory, logger);
            await ExerciseRestrictionSeeder.SeedAsync(context, dataDirectory, logger);
            await ExerciseNameSyncSeeder.SeedAsync(context, dataDirectory, logger);
            await ExerciseVariantSeeder.SeedAsync(context, dataDirectory, logger);
            await EnduranceExerciseSeeder.SeedAsync(context, logger);
            await ExerciseTypeClassificationSeeder.SeedAsync(context, dataDirectory, logger);
        }
        else
        {
            // En proveedores no relacionales (p.ej. InMemory en tests) se omiten migraciones/seeders físicos.
            logger.LogInformation("Se omitieron migraciones y seeders porque el proveedor de base de datos no es relacional.");
        }
    }
}
