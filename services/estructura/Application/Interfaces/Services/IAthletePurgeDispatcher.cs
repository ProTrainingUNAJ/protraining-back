using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface IAthletePurgeDispatcher
{
    Task<bool> DispatchPurgeToExternalServicesAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);
}
