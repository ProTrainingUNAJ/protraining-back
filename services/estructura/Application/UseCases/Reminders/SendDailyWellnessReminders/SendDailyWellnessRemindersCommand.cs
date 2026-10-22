using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Services;
using Domain.Common;


namespace Application.UseCases.Reminders.SendDailyWellnessReminders;

public sealed record SendDailyWellnessRemindersCommand(DateOnly? TargetDate = null);

public sealed record SendDailyWellnessRemindersResult(
    int RemindersSent,
    int SkippedAlreadySent,
    int SkippedAlreadyCheckedIn);

public sealed class SendDailyWellnessRemindersCommandHandler(
    ISessionScheduleService sessionScheduleService,
    IWellnessCheckInService wellnessCheckInService,
    IWellnessReminderTracker reminderTracker,
    IPushNotificationService pushNotificationService)
{
    public const string ReminderTitle = "Recordatorio de Wellness";
    public const string ReminderMessage = "No cargaste cómo estás; tu preparador necesita saberlo";

    public async Task<SendDailyWellnessRemindersResult> HandleAsync(
        SendDailyWellnessRemindersCommand command,
        CancellationToken cancellationToken = default)
    {
        var today = command.TargetDate ?? ArgentinaTime.Today;

        // 1. busca atletas que tienen sesion en el calendario hoy
        var athleteIdsWithSession = await sessionScheduleService.GetAthletesWithScheduledSessionAsync(today, cancellationToken);
        if (athleteIdsWithSession.Count == 0)
        {
            return new SendDailyWellnessRemindersResult(0, 0, 0);
        }

        int sentCount = 0;
        int alreadySentCount = 0;
        int alreadyCheckedInCount = 0;

        foreach (var athleteId in athleteIdsWithSession)
        {
            // 2. si el atleta ya hizo su check-in hoy, no recibe nada
            var hasCheckedIn = await wellnessCheckInService.HasCompletedCheckInAsync(athleteId, today, cancellationToken);
            if (hasCheckedIn)
            {
                alreadyCheckedInCount++;
                continue;
            }

            // 3. regla de negocio: maximo un solo recordatorio por dia por atleta
            var alreadyReminded = await reminderTracker.HasReminderBeenSentAsync(athleteId, today, cancellationToken);
            if (alreadyReminded)
            {
                alreadySentCount++;
                continue;
            }

            // 4. envia el recordatorio por push
            await pushNotificationService.SendNotificationAsync(
                athleteId,
                ReminderTitle,
                ReminderMessage,
                cancellationToken);

            // 5. registra el envio para no repetir en el dia
            await reminderTracker.MarkReminderAsSentAsync(athleteId, today, cancellationToken);
            sentCount++;
        }

        return new SendDailyWellnessRemindersResult(sentCount, alreadySentCount, alreadyCheckedInCount);
    }
}
