using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface IExternalServicePurgeClient
{
    Task<ServicePurgeResult> PurgeAthleteInServiceAsync(
        string serviceName,
        string baseUrl,
        Guid athleteId,
        CancellationToken cancellationToken = default);
}

