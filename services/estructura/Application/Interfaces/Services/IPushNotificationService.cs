using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface IPushNotificationService
{
    Task SendNotificationAsync(Guid athleteId, string title, string message, CancellationToken cancellationToken = default);
}
