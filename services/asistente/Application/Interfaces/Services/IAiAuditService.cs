using Domain.Entities;

namespace Application.Interfaces.Services;

/// <summary>
/// Service contract to asynchronously persist AI invocation logs and metrics into the database.
/// </summary>
public interface IAiAuditService
{
    Task LogAsync(AiLog log, CancellationToken cancellationToken = default);
}
