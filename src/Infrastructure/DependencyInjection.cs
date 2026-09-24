using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProTraining.Application.Common;
using ProTraining.Infrastructure.Analysis;
using ProTraining.Infrastructure.Auth;
using ProTraining.Infrastructure.Background;
using ProTraining.Infrastructure.Data;

namespace ProTraining.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<JwtService>();
        services.AddScoped<IProveedorDeAnalisis, AnalysisProvider>();

        // Worker en segundo plano para avisos preventivos
        services.AddHostedService<AlertsBackgroundService>();

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fallback para pruebas locales en caso de no contar con PostgreSQL levantado
            connectionString = "Host=localhost;Port=5432;Database=protraining_db;Username=protraining_user;Password=protraining_secret_pass_123";
        }

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            });
        });

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        return services;
    }
}
