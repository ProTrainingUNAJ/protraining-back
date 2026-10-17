using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class PushSubscriptionRepository(AppDbContext context) : IPushSubscriptionRepository
{
    public async Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.PushSubscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Endpoint == endpoint && p.DeletedAt == null, cancellationToken));

    public async Task<IReadOnlyList<PushSubscription>> GetActiveByAthleteIdAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.PushSubscriptions
                .IgnoreQueryFilters()
                .Where(p => p.AthleteId == athleteId && p.IsActive && p.DeletedAt == null)
                .ToListAsync(cancellationToken));

    public async Task<IReadOnlyList<PushSubscription>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await PersistenceErrors.ExecuteAsync(async () =>
            await context.PushSubscriptions
                .IgnoreQueryFilters()
                .Where(p => p.UserId == userId && p.IsActive && p.DeletedAt == null)
                .ToListAsync(cancellationToken));

    public Task AddAsync(PushSubscription subscription, CancellationToken cancellationToken = default)
    {
        context.PushSubscriptions.Add(subscription);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PushSubscription subscription, CancellationToken cancellationToken = default)
    {
        context.PushSubscriptions.Update(subscription);
        return Task.CompletedTask;
    }
}
