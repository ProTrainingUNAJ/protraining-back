using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public sealed record AthletePurgeDispatchResult(bool IsSuccess, string? ErrorMessage = null);

public interface IAthletePurgeDispatcher
{
    Task<AthletePurgeDispatchResult> DispatchPurgeToExternalServicesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);
}
