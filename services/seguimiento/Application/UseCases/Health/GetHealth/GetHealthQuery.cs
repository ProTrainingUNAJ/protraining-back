using Application.DTOs;
using Application.Interfaces.Services;

namespace Application.UseCases.Health.GetHealth;

public sealed record GetHealthQuery;

public sealed class GetHealthQueryHandler(IDatabaseHealthService databaseHealthService)
{
    public async Task<HealthDto> HandleAsync(GetHealthQuery query, CancellationToken cancellationToken = default)
    {
        var database = await databaseHealthService.CanConnectAsync(cancellationToken);
        return new HealthDto(database ? "Healthy" : "Degraded", database ? "Connected" : "Unavailable");
    }
}
