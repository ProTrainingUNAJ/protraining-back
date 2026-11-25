namespace Application.Interfaces.Services;

/// <summary>
/// Ejecuta una operación contra <see cref="IAiService"/> dentro de un scope de dependencias aislado,
/// evitando compartir servicios scoped (como DbContext o auditoría) entre tareas concurrentes (KAN-491).
/// </summary>
public interface IScopedAiServiceRunner
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<IAiService, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
