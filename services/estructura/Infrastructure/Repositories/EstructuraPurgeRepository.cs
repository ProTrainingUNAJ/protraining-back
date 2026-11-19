using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Interfaces.Persistence;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public sealed class EstructuraPurgeRepository(AppDbContext context) : IEstructuraPurgeRepository
{
    public async Task PurgeAthleteLocalDataAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        await PersistenceErrors.ExecuteAsync(async () =>
        {
            // 1. Suscripciones push
            await context.PushSubscriptions
                .IgnoreQueryFilters()
                .Where(p => p.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 2. Recordatorios de wellness enviados (no hereda de BaseEntity ni tiene filtro de tenant)
            await context.WellnessRemindersSent
                .Where(w => w.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 3. Vínculos con categorías
            await context.AthleteCategories
                .IgnoreQueryFilters()
                .Where(ac => ac.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 4. Fotos de perfil
            await context.AthletePhotos
                .IgnoreQueryFilters()
                .Where(ap => ap.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 5. Registros de auditoría médica
            await context.MedicalRecordAuditLogs
                .IgnoreQueryFilters()
                .Where(m => m.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 6. Fichas médicas
            await context.MedicalRecords
                .IgnoreQueryFilters()
                .Where(m => m.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 7. Consentimientos digitales
            await context.DigitalConsents
                .IgnoreQueryFilters()
                .Where(dc => dc.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 8. Registro del atleta
            await context.Athletes
                .IgnoreQueryFilters()
                .Where(a => a.Id == athleteId)
                .ExecuteDeleteAsync(cancellationToken);
        });
    }
}

