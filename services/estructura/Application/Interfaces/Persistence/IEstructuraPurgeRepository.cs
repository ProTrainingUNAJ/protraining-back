using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Persistence;

public interface IEstructuraPurgeRepository
{
    Task PurgeAthleteLocalDataAsync(Guid athleteId, CancellationToken cancellationToken = default);
}

