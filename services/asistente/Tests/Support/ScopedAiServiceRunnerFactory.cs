using Application.Interfaces.Services;

namespace Asistente.Tests;

/// <summary>
/// Adaptador liviano de <see cref="IScopedAiServiceRunner"/> para pruebas unitarias que utilizan mocks de <see cref="IAiService"/>.
/// </summary>
internal static class ScopedAiServiceRunnerFactory
{
    public static IScopedAiServiceRunner From(IAiService aiService) =>
        new DirectAiServiceRunner(aiService);

    private sealed class DirectAiServiceRunner(IAiService aiService) : IScopedAiServiceRunner
    {
        public Task<TResult> ExecuteAsync<TResult>(
            Func<IAiService, CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            operation(aiService, cancellationToken);
    }
}
