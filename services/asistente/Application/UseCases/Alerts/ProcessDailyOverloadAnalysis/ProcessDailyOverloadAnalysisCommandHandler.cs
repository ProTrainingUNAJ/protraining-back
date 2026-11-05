using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;

/// <summary>
/// Caso de uso que orquesta el análisis nocturno de check-ins de atletas cruzado con el fixture (#177 · KAN-472 / #178 · KAN-473).
/// Identifica patrones de alarma (RPE alto sostenido, déficit de sueño, fatiga crítica y proximidad de partido en D-2) y emite avisos preventivos.
/// Procesa y persiste las alertas agrupadas por Preparador Físico mediante UsePfContext para respetar el aislamiento multi-tenant.
/// </summary>
public sealed class ProcessDailyOverloadAnalysisCommandHandler(
    IStructureAthleteClient structureAthleteClient,
    ISeguimientoClient seguimientoClient,
    IStructureCalendarClient structureCalendarClient,
    IPreventiveAlertRepository alertRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ILogger<ProcessDailyOverloadAnalysisCommandHandler> logger)
{
    public async Task<ProcessDailyOverloadAnalysisResult> HandleAsync(
        ProcessDailyOverloadAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        var targetDate = command.TargetDate ?? ArgentinaTime.Today;
        var fromDate = targetDate.AddDays(-6);
        var toDate = targetDate;
        var matchInTwoDaysTarget = targetDate.AddDays(2);

        logger.LogInformation(
            "Iniciando análisis diario de sobrecarga y calendario para la ventana {FromDate:yyyy-MM-dd} a {ToDate:yyyy-MM-dd} (D-2: {D2Date:yyyy-MM-dd}).",
            fromDate,
            toDate,
            matchInTwoDaysTarget);

        // 1. Obtener los atletas activos desde Estructura (incluye su PfId)
        var athletes = await structureAthleteClient.GetAthletesAsync(cancellationToken);
        if (athletes.Count == 0)
        {
            logger.LogInformation("No se encontraron atletas activos para procesar en el análisis nocturno.");
            return new ProcessDailyOverloadAnalysisResult(0, 0, 0);
        }

        var athleteMap = athletes.ToDictionary(a => a.Id);

        // 2. Obtener el historial de check-ins de los últimos 7 días desde Seguimiento
        var checkIns = await seguimientoClient.GetAthleteCheckInsHistoryAsync(
            athleteMap.Keys.ToList(),
            fromDate,
            toDate,
            cancellationToken);

        if (checkIns.Count == 0)
        {
            logger.LogInformation("No se registraron check-ins en la ventana de análisis para los atletas activos.");
            return new ProcessDailyOverloadAnalysisResult(athletes.Count, 0, 0);
        }

        // 3. Consultar calendario de partidos próximos en Estructura para correlación D-2 (#178 · KAN-473)
        var categoryIds = athletes
            .Where(a => a.CategoryId.HasValue && a.CategoryId.Value != Guid.Empty)
            .Select(a => a.CategoryId!.Value)
            .Distinct()
            .ToList();

        var matchDatesByCategory = new Dictionary<Guid, IReadOnlyList<DateOnly>>();
        foreach (var categoryId in categoryIds)
        {
            var matchDates = await structureCalendarClient.GetMatchDatesAsync(
                categoryId,
                targetDate,
                matchInTwoDaysTarget,
                cancellationToken);
            matchDatesByCategory[categoryId] = matchDates;
        }

        var checkInsByAthlete = checkIns
            .GroupBy(c => c.AthleteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int totalGeneratedAlerts = 0;
        int totalSkippedAlerts = 0;

        // 4. Procesar y guardar alertas agrupadas por Preparador Físico (aislamiento multi-tenant)
        var athletesByPf = athletes.GroupBy(a => a.PfId);

        foreach (var pfGroup in athletesByPf)
        {
            var pfId = pfGroup.Key;

            using (currentUser.UsePfContext(pfId))
            {
                // Consultar alertas pendientes dentro del contexto del PF actual
                var pendingAlerts = await alertRepository.ListAsync(
                    onlyUnreviewed: true,
                    severity: null,
                    cancellationToken: cancellationToken);

                int pfGeneratedAlerts = 0;

                foreach (var athlete in pfGroup)
                {
                    if (!checkInsByAthlete.TryGetValue(athlete.Id, out var athleteCheckIns) || athleteCheckIns.Count == 0)
                    {
                        continue;
                    }

                    var records = athleteCheckIns.Select(c => new AthleteCheckInRecord(
                        c.Date,
                        c.SleepHours,
                        c.SleepQuality,
                        c.MuscleSoreness,
                        c.FatigueLevel,
                        c.MoodRate,
                        c.StressLevel,
                        c.EffortRate,
                        c.IsSick,
                        c.PainZones,
                        c.HasActiveInjury
                    )).ToList();

                    // Verificar si la categoría del atleta tiene partido en D-2 (dentro de 2 días)
                    bool hasMatchInTwoDays = false;
                    if (athlete.CategoryId.HasValue &&
                        matchDatesByCategory.TryGetValue(athlete.CategoryId.Value, out var categoryMatches))
                    {
                        hasMatchInTwoDays = categoryMatches.Contains(matchInTwoDaysTarget);
                    }

                    var patternResult = OverloadPatternDetector.Detect(
                        athlete.Id,
                        records,
                        hasMatchInTwoDays: hasMatchInTwoDays,
                        matchDate: hasMatchInTwoDays ? matchInTwoDaysTarget : null);

                    if (!patternResult.HasPattern)
                    {
                        continue;
                    }

                    // Evitar duplicados: verificar si ya existe una alerta pendiente para este atleta y este tipo
                    var alreadyPending = pendingAlerts.Any(a =>
                        a.AthleteId == athlete.Id &&
                        a.Type == patternResult.Type &&
                        a.Status == AlertStatus.Pending);

                    if (alreadyPending)
                    {
                        logger.LogInformation(
                            "Omitiendo alerta para el atleta {AthleteId}: ya existe una alerta pendiente de tipo {Type} (PF {PfId}).",
                            athlete.Id,
                            patternResult.Type,
                            pfId);
                        totalSkippedAlerts++;
                        continue;
                    }

                    var alert = PreventiveAlert.Create(
                        athleteId: athlete.Id,
                        athleteName: athlete.FullName,
                        reason: patternResult.Reason,
                        suggestion: patternResult.Suggestion,
                        severity: patternResult.Severity,
                        type: patternResult.Type,
                        suggestedAdjustmentType: patternResult.SuggestedAdjustmentType,
                        suggestedAdjustmentPercentage: patternResult.SuggestedAdjustmentPercentage,
                        categoryId: athlete.CategoryId,
                        generatedAtUtc: DateTime.UtcNow);

                    await alertRepository.AddAsync(alert, cancellationToken);
                    pfGeneratedAlerts++;
                    totalGeneratedAlerts++;

                    logger.LogInformation(
                        "Alerta preventiva generada para el atleta {AthleteId}: {Type} con severidad {Severity} (PF {PfId}).",
                        athlete.Id,
                        patternResult.Type,
                        patternResult.Severity,
                        pfId);
                }

                if (pfGeneratedAlerts > 0)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }
        }

        logger.LogInformation(
            "Análisis nocturno completado. Atletas evaluados: {Athletes}, Alertas generadas: {Generated}, Omitidas por duplicado: {Skipped}.",
            athletes.Count,
            totalGeneratedAlerts,
            totalSkippedAlerts);

        return new ProcessDailyOverloadAnalysisResult(athletes.Count, totalGeneratedAlerts, totalSkippedAlerts);
    }
}
