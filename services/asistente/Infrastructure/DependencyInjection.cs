using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Infrastructure.Data;
using Infrastructure.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Ollama;
using Infrastructure.Services.Ai.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<IAiAuditService, AiAuditService>();
        services.AddScoped<IRoutineDraftRepository, Infrastructure.Data.Repositories.RoutineDraftRepository>();

        services.AddMemoryCache();
        services.Configure<Infrastructure.Services.Ai.Cache.AiCacheOptions>(configuration.GetSection(Infrastructure.Services.Ai.Cache.AiCacheOptions.SectionName));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));

        services.AddSingleton<IAiResponseCache, Infrastructure.Services.Ai.Cache.AiResponseMemoryCache>();
        services.AddHttpClient<OllamaClient>();
        services.AddHttpClient<GeminiClient>();
        services.AddScoped<RuleBasedAiClient>();
        services.AddScoped<IExerciseSemanticValidator, Application.Common.Ai.ExerciseSemanticValidator>();
        services.AddScoped<IAiService, ResilientAiService>();

        return services;
    }

    // Applies pending migrations on startup. Does nothing if the service has no migrations yet.
    public static async Task ApplyMigrationsAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Database.GetMigrations().Any())
            await context.Database.MigrateAsync();
    }
}
