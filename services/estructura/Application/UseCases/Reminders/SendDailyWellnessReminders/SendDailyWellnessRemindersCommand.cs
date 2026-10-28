using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;

namespace Application.UseCases.Reminders.SendDailyWellnessReminders;

public sealed record SendDailyWellnessRemindersCommand(DateOnly? TargetDate = null);

public sealed record SendDailyWellnessRemindersResult(
    int RemindersSent,
    int SkippedAlreadySent,
    int SkippedAlreadyCheckedIn);

public sealed class SendDailyWellnessRemindersCommandHandler(
    IAthleteRepository athleteRepository,
    ICurrentUser currentUser,
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

        // 1. Obtiene los PFs que tienen atletas registrados
        var pfIds = await athleteRepository.GetDistinctPfIdsAsync(cancellationToken);
        if (pfIds.Count == 0)
        {
            return new SendDailyWellnessRemindersResult(0, 0, 0);
        }

        int sentCount = 0;
        int alreadySentCount = 0;
        int alreadyCheckedInCount = 0;

        foreach (var pfId in pfIds)
        {
            using (currentUser.UsePfContext(pfId))
            {
                // 2. Busca atletas de este PF que tienen sesión en el calendario hoy
                var athleteIdsWithSession = await sessionScheduleService.GetAthletesWithScheduledSessionAsync(today, pfId, cancellationToken);
                if (athleteIdsWithSession.Count == 0)
                {
                    continue;
                }

                // 3. Consulta en lote a Seguimiento los atletas que ya completaron check-in hoy
                var completedAthleteIds = await wellnessCheckInService.GetCompletedCheckInAthleteIdsAsync(
                    athleteIdsWithSession,
                    today,
                    pfId,
                    cancellationToken);

                foreach (var athleteId in athleteIdsWithSession)
                {
                    // 4. Si el atleta ya hizo su check-in hoy, no recibe nada
                    if (completedAthleteIds.Contains(athleteId))
                    {
                        alreadyCheckedInCount++;
                        continue;
                    }

                    // 5. Regla de negocio: maximo un solo recordatorio por dia por atleta
                    var alreadyReminded = await reminderTracker.HasReminderBeenSentAsync(athleteId, today, cancellationToken);
                    if (alreadyReminded)
                    {
                        alreadySentCount++;
                        continue;
                    }

                    // 6. Envia el recordatorio por push
                    await pushNotificationService.SendNotificationAsync(
                        athleteId,
                        ReminderTitle,
                        ReminderMessage,
                        cancellationToken);

                    // 7. Registra el envio para no repetir en el día
                    await reminderTracker.MarkReminderAsSentAsync(athleteId, today, cancellationToken);
                    sentCount++;
                }
            }
        }

        return new SendDailyWellnessRemindersResult(sentCount, alreadySentCount, alreadyCheckedInCount);
    }
}