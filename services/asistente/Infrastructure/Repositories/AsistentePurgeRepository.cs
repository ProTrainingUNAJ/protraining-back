using Application.DTOs.Internal;
using Application.Interfaces.Persistence;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>
/// Implementación de purga física de datos de un atleta en Asistente (#205 · KAN-500).
/// Utiliza IgnoreQueryFilters() para operar en contexto interno (rol Servicio) sin un PF autenticado.
/// </summary>
public sealed class AsistentePurgeRepository(AppDbContext context) : IAsistentePurgeRepository
{
    public async Task<PurgedAthleteDataDto> PurgeAthleteDataAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        await using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        // 1. Borradores de rutina (RoutineDrafts)
        int routineDraftsCount = await context.RoutineDrafts.IgnoreQueryFilters()
            .Where(r => r.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 2. Borradores de planes multisesión del atleta (PlanDrafts)
        int planDraftsCount = await context.PlanDrafts.IgnoreQueryFilters()
            .Where(p => p.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 3. Propuestas de ajuste del día (DayAdjustments)
        int dayAdjustmentsCount = await context.DayAdjustments.IgnoreQueryFilters()
            .Where(d => d.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 4. Recomendaciones generadas para el atleta (AthleteRecommendations)
        int athleteRecommendationsCount = await context.AthleteRecommendations.IgnoreQueryFilters()
            .Where(r => r.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 5. Avisos preventivos del atleta (PreventiveAlerts)
        int preventiveAlertsCount = await context.PreventiveAlerts.IgnoreQueryFilters()
            .Where(a => a.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 6. Decisiones del PF sobre propuestas asociadas al atleta (PfDecisions)
        int pfDecisionsCount = await context.PfDecisions.IgnoreQueryFilters()
            .Where(d => d.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        // 7. Registros de llamadas a la IA y auditoría (AiLogs)
        int aiLogsCount = await context.AiLogs
            .Where(l => l.AthleteId == athleteId)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        int totalRecordsPurged = routineDraftsCount
            + planDraftsCount
            + dayAdjustmentsCount
            + athleteRecommendationsCount
            + preventiveAlertsCount
            + pfDecisionsCount
            + aiLogsCount;

        return new PurgedAthleteDataDto(
            athleteId,
            routineDraftsCount,
            planDraftsCount,
            dayAdjustmentsCount,
            athleteRecommendationsCount,
            preventiveAlertsCount,
            pfDecisionsCount,
            aiLogsCount,
            totalRecordsPurged);
    }
}
