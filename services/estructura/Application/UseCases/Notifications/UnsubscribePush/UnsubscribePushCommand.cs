using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Notifications.UnsubscribePush;

public sealed record UnsubscribePushCommand(string Endpoint);

public sealed class UnsubscribePushCommandHandler(
    IPushSubscriptionRepository subscriptionRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(UnsubscribePushCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException("Debe estar autenticado para desuscribirse.");

        var existing = await subscriptionRepository.GetByEndpointAsync(command.Endpoint, cancellationToken);

        if (existing is not null && existing.UserId == userId)
        {
            existing.Deactivate();
            await subscriptionRepository.UpdateAsync(existing, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
