using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.Notifications.RegisterPushSubscription;

public sealed record RegisterPushSubscriptionCommand(
    string Endpoint,
    string P256dh,
    string Auth,
    string? UserAgent = null);

public sealed class RegisterPushSubscriptionCommandHandler(
    IPushSubscriptionRepository subscriptionRepository,
    IAthleteRepository athleteRepository,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task HandleAsync(RegisterPushSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException("Debe estar autenticado para registrar una suscripción.");

        // si el usuario autenticado es un atleta, vinculamos su AthleteId para despachar recordatorios directamente
        var athlete = await athleteRepository.GetByAccountIdAsync(userId, cancellationToken);
        Guid? athleteId = athlete?.Id;

        var existing = await subscriptionRepository.GetByEndpointAsync(command.Endpoint, cancellationToken);

        if (existing is not null)
        {
            if (existing.UserId == userId)
            {
                existing.UpdateKeys(command.P256dh, command.Auth, command.UserAgent);
                if (athleteId.HasValue && !existing.AthleteId.HasValue)
                {
                    existing.AssignToUser(userId, athleteId);
                }
            }
            else
            {
                // el navegador cambio de usuario; reasignamos la suscripcion para no emitir avisos cruzados
                existing.AssignToUser(userId, athleteId);
                existing.UpdateKeys(command.P256dh, command.Auth, command.UserAgent);
            }

            await subscriptionRepository.UpdateAsync(existing, cancellationToken);
        }
        else
        {
            var subscription = PushSubscription.Create(
                userId,
                athleteId,
                command.Endpoint,
                command.P256dh,
                command.Auth,
                command.UserAgent);

            await subscriptionRepository.AddAsync(subscription, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
