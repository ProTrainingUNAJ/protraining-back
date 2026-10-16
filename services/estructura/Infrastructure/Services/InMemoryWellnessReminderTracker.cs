using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;

namespace Infrastructure.Services;

public sealed class InMemoryWellnessReminderTracker : IWellnessReminderTracker
{
    // almacena el par (atletaId, fecha) de recordatorios ya enviados de forma segura para hilos
    private readonly ConcurrentDictionary<(Guid AthleteId, DateOnly Date), bool> _sentReminders = new();

    public Task<bool> HasReminderBeenSentAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var alreadySent = _sentReminders.ContainsKey((athleteId, date));
        return Task.FromResult(alreadySent);
    }

    public Task MarkReminderAsSentAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        _sentReminders.TryAdd((athleteId, date), true);
        return Task.CompletedTask;
    }
}
