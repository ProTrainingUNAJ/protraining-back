using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.Athletes.PurgePendingAthletes;

public sealed record ProcessPendingPurgesCommand(int? RetentionDays = null);

public sealed record ProcessPendingPurgesResult(
    int TotalEvaluated,
    int PurgedSuccessfully,
    int FailedOrPostponed,
    int SkippedWithinRetention);

public sealed class ProcessPendingPurgesCommandHandler(
    IPendingPurgeRepository pendingPurgeRepository,
    IAthletePurgeDispatcher purgeDispatcher,
    IEstructuraPurgeRepository estructuraPurgeRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int DefaultRetentionDays = 30;

    public async Task<ProcessPendingPurgesResult> HandleAsync(
        ProcessPendingPurgesCommand command,
        CancellationToken cancellationToken = default)
    {
        var retentionDays = command.RetentionDays ?? DefaultRetentionDays;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoffDate = now.AddDays(-retentionDays);

        var pendingList = await pendingPurgeRepository.ListPendingAsync(cancellationToken);

        int totalEvaluated = 0;
        int purgedSuccessfully = 0;
        int failedOrPostponed = 0;
        int skippedWithinRetention = 0;

        foreach (var purge in pendingList)
        {
            totalEvaluated++;

            // Criterio 1: Solo se purga si el atleta fue borrado hace más del plazo definido
            if (purge.CreatedAt > cutoffDate)
            {
                skippedWithinRetention++;
                continue;
            }

            // Despacha la purga a los 4 microservicios externos
            var externalSuccess = await purgeDispatcher.DispatchPurgeToExternalServicesAsync(
                purge.AthleteId,
                cancellationToken);

            // Criterio 2: Si algún servicio externo falla o está caído, no se pierde el pedido y se reintenta más tarde
            if (!externalSuccess)
            {
                failedOrPostponed++;
                continue;
            }

            // Si todos los externos respondieron con éxito (o 404), purga los datos locales en Estructura (5to servicio)
            await estructuraPurgeRepository.PurgeAthleteLocalDataAsync(purge.AthleteId, cancellationToken);

            // Marca la purga como completada en la tabla pending_purges
            purge.MarkAsProcessed();
            await unitOfWork.SaveChangesAsync(cancellationToken);

            purgedSuccessfully++;
        }

        return new ProcessPendingPurgesResult(
            totalEvaluated,
            purgedSuccessfully,
            failedOrPostponed,
            skippedWithinRetention);
    }
}
