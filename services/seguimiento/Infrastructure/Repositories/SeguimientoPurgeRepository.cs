using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Implementación de purga física de datos de un atleta en Seguimiento (#204 · KAN-499).
/// Utiliza IgnoreQueryFilters() para operar en contexto interno (rol Servicio) sin un PF autenticado.
/// </summary>
public sealed class SeguimientoPurgeRepository(AppDbContext context) : ISeguimientoPurgeRepository
{
    public async Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        return await PersistenceErrors.ExecuteAsync(async () =>
        {
            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

            var checkInIds = await context.CheckIns.IgnoreQueryFilters()
                .Where(c => c.AthleteId == athleteId)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            var injuryIds = await context.AthleteInjuries.IgnoreQueryFilters()
                .Where(i => i.AthleteId == athleteId)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            // 1. Eliminar dolores de check-in asociados al atleta (por checkInId o por injuryId)
            int checkInInjuryPainsCount = 0;
            if (checkInIds.Count > 0 || injuryIds.Count > 0)
            {
                checkInInjuryPainsCount = await context.CheckInInjuryPains.IgnoreQueryFilters()
                    .Where(p => checkInIds.Contains(p.CheckInId) || injuryIds.Contains(p.InjuryId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // 2. Eliminar check-ins
            int checkInsCount = 0;
            if (checkInIds.Count > 0)
            {
                checkInsCount = await context.CheckIns.IgnoreQueryFilters()
                    .Where(c => c.AthleteId == athleteId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // 3. Eliminar cambios de estado de lesiones
            int injuryStatusChangesCount = 0;
            if (injuryIds.Count > 0)
            {
                injuryStatusChangesCount = await context.AthleteInjuryStatusChanges.IgnoreQueryFilters()
                    .Where(c => injuryIds.Contains(c.InjuryId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // 4. Eliminar lesiones
            int injuriesCount = 0;
            if (injuryIds.Count > 0)
            {
                injuriesCount = await context.AthleteInjuries.IgnoreQueryFilters()
                    .Where(i => i.AthleteId == athleteId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // 5. Eliminar cargas diarias
            int dailyLoadsCount = await context.DailyLoads.IgnoreQueryFilters()
                .Where(d => d.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 6. Eliminar resultados de tests de campo
            int fieldTestResultsCount = await context.FieldTestResults.IgnoreQueryFilters()
                .Where(f => f.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            // 7. Eliminar asistencias
            int attendancesCount = await context.Attendances.IgnoreQueryFilters()
                .Where(a => a.AthleteId == athleteId)
                .ExecuteDeleteAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            int totalRecordsPurged = checkInsCount
                + injuriesCount
                + dailyLoadsCount
                + fieldTestResultsCount
                + attendancesCount
                + checkInInjuryPainsCount
                + injuryStatusChangesCount;

            return new PurgedAthleteDataDto(
                athleteId,
                checkInsCount,
                dailyLoadsCount,
                injuriesCount,
                fieldTestResultsCount,
                attendancesCount,
                totalRecordsPurged,
                checkInInjuryPainsCount,
                injuryStatusChangesCount);
        });
    }
}

