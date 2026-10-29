using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services;

public sealed class DbWellnessReminderTracker(AppDbContext context) : IWellnessReminderTracker
{
    public async Task<bool> HasReminderBeenSentAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        // verifica en la tabla wellness_reminders_sent si ya existe el registro para ese atleta y fecha
        return await context.WellnessRemindersSent
            .AsNoTracking()
            .AnyAsync(w => w.AthleteId == athleteId && w.Date == date, cancellationToken);
    }

    public async Task MarkReminderAsSentAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
    {
        // persiste el envio en la base de datos
        var reminder = WellnessReminderSent.Create(athleteId, date);
        context.WellnessRemindersSent.Add(reminder);
        await context.SaveChangesAsync(cancellationToken);
    }
}
