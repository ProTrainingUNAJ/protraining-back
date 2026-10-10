namespace Application.Interfaces.Services;

public interface IDatabaseHealthService
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
