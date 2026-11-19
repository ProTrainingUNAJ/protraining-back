using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Infrastructure.Data;
using Infrastructure.Services;
using Infrastructure.Services.Ai;
using Infrastructure.Services.Ai.Agent;
using Infrastructure.Services.Ai.Gemini;
using Infrastructure.Services.Ai.Rules;
using Infrastructure.Services.Adaptation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        services.AddScoped<IPlanDraftRepository, Infrastructure.Data.Repositories.PlanDraftRepository>();
        services.AddScoped<IPfDecisionRepository, Infrastructure.Data.Repositories.PfDecisionRepository>();
        services.AddScoped<IPreventiveAlertRepository, Infrastructure.Data.Repositories.PreventiveAlertRepository>();
        services.AddScoped<IDayAdjustmentRepository, Infrastructure.Data.Repositories.DayAdjustmentRepository>();
        services.AddScoped<IAthleteRecommendationRepository, Infrastructure.Data.Repositories.AthleteRecommendationRepository>();
        services.AddScoped<IAsistentePurgeRepository, Infrastructure.Data.Repositories.AsistentePurgeRepository>();
        services.AddScoped<IMatchDayLoadValidator, Application.Common.PlanDrafts.MatchDayLoadValidator>();

        services.AddMemoryCache();
        services.AddHttpClient();
        services.Configure<Infrastructure.Services.Ai.Cache.AiCacheOptions>(configuration.GetSection(Infrastructure.Services.Ai.Cache.AiCacheOptions.SectionName));
        services.Configure<Infrastructure.Services.Ai.Quota.AiQuotaOptions>(configuration.GetSection(Infrastructure.Services.Ai.Quota.AiQuotaOptions.SectionName));
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));

        services.AddSingleton<IAiResponseCache, Infrastructure.Services.Ai.Cache.AiResponseMemoryCache>();
        services.AddSingleton<IAiQuotaService, Infrastructure.Services.Ai.Quota.AiQuotaService>();
        services.AddScoped<RuleBasedAiClient>();
        services.AddScoped<IExerciseSemanticValidator, Application.Common.Ai.ExerciseSemanticValidator>();

        // Cadena ordenada de proveedores del agente (OpenAI -> Gemini -> Reglas)
        services.AddScoped<IReadOnlyList<IAiClient>>(sp =>
        {
            var agentOptions = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var mcpClient = sp.GetRequiredService<IExerciseLibraryMcpClient>();
            var auditService = sp.GetRequiredService<IAiAuditService>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var catalogService = sp.GetService<IExerciseCatalogService>();
            var ruleBasedClient = sp.GetRequiredService<RuleBasedAiClient>();

            var list = new List<IAiClient>();

            foreach (var provider in agentOptions.Providers)
            {
                var resolvedKey = provider.ApiKey;
                if (string.IsNullOrWhiteSpace(resolvedKey))
                {
                    if (provider.Name.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    }
                    else if (provider.Name.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? configuration["Gemini:ApiKey"];
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedKey))
                {
                    // Si no tiene clave configurada, se saltea sin error según protocolo
                    continue;
                }

                var providerConfig = new AgentProviderOptions
                {
                    Name = provider.Name,
                    BaseUrl = provider.BaseUrl,
                    Model = provider.Model,
                    ApiKey = resolvedKey,
                    TimeoutSeconds = provider.TimeoutSeconds > 0 ? provider.TimeoutSeconds : agentOptions.TimeoutSeconds
                };

                var httpClient = httpClientFactory.CreateClient($"Agent_{provider.Name}");
                var agentLogger = loggerFactory.CreateLogger<GenericAgentAiClient>();

                list.Add(new GenericAgentAiClient(
                    providerConfig,
                    httpClient,
                    mcpClient,
                    auditService,
                    agentLogger,
                    catalogService,
                    agentOptions.MaxToolCalls));
            }

            // Motor de reglas determinísticas locales como respaldo final garantizado
            list.Add(ruleBasedClient);
            return list;
        });

        services.AddScoped<IAiService, ResilientAiService>();
        services.AddScoped<IAthleteAdaptationContextService, AthleteAdaptationContextService>();

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
