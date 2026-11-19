using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Implementación de purga física de datos de un atleta en Entrenamiento (#204 · KAN-499).
/// Utiliza IgnoreQueryFilters() para operar en contexto interno (rol Servicio) sin un PF autenticado.
/// </summary>
public sealed class EntrenamientoPurgeRepository(AppDbContext context) : IEntrenamientoPurgeRepository
{
    public async Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        // 1. Eliminar series ejecutadas (PerformedSets)
        int performedSetsCount = await context.PerformedSets.IgnoreQueryFilters()
            .Where(p => p.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 2. Eliminar propuestas de récord (RecordProposals)
        int recordProposalsCount = await context.RecordProposals.IgnoreQueryFilters()
            .Where(r => r.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 3. Eliminar récords del atleta (AthleteRecords)
        int athleteRecordsCount = await context.AthleteRecords.IgnoreQueryFilters()
            .Where(r => r.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 4. Eliminar ajustes de sesión del atleta (AthleteSessionAdjustments)
        int sessionAdjustmentsCount = await context.AthleteSessionAdjustments.IgnoreQueryFilters()
            .Where(a => a.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 5. Eliminar asignaciones de planes directas al atleta (PlanAssignments)
        int planAssignmentsCount = await context.PlanAssignments.IgnoreQueryFilters()
            .Where(p => p.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        int totalRecordsPurged = performedSetsCount + recordProposalsCount + athleteRecordsCount + sessionAdjustmentsCount + planAssignmentsCount;

        return new PurgedAthleteDataDto(
            athleteId,
            performedSetsCount,
            recordProposalsCount,
            athleteRecordsCount,
            sessionAdjustmentsCount,
            planAssignmentsCount,
            totalRecordsPurged);
    }
}

