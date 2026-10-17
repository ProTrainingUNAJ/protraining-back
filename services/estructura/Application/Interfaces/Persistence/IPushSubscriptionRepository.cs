using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Interfaces.Persistence;

public interface IPushSubscriptionRepository
{
    Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PushSubscription>> GetActiveByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PushSubscription>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(PushSubscription subscription, CancellationToken cancellationToken = default);
    Task UpdateAsync(PushSubscription subscription, CancellationToken cancellationToken = default);
}
