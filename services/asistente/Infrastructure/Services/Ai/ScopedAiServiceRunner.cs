using Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.Ai;

/// <summary>
/// Resuelve una instancia aislada de <see cref="IAiService"/> dentro de un nuevo <see cref="AsyncServiceScope"/>
/// para corridas paralelas por atleta sin compartir servicios scoped entre hilos (KAN-491).
/// </summary>
public sealed class ScopedAiServiceRunner(IServiceScopeFactory scopeFactory) : IScopedAiServiceRunner
{
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<IAiService, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = scopeFactory.CreateAsyncScope();
        var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
        return await operation(aiService, cancellationToken);
    }
}
