using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Interfaces.Services;

public interface IWellnessReminderTracker
{
    // Verifica si ya se le envio un recordatorio al atleta en el dia
    Task<bool> HasReminderBeenSentAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default);

    // Registra que se envio el recordatorio para no volver a mandarlo en el mismo dia
    Task MarkReminderAsSentAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default);
}

