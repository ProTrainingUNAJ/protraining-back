using Application.Interfaces.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.HealthChecks;

public sealed class DatabaseHealthCheck(IDatabaseHealthService databaseHealth) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await databaseHealth.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Base de datos conectada.")
            : HealthCheckResult.Unhealthy("Base de datos no disponible.");
}
