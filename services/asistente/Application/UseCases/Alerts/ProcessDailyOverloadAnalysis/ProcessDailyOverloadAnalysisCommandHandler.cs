using Application.DTOs.Alerts;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.UseCases.Alerts.ProcessDailyOverloadAnalysis;

/// <summary>
/// Caso de uso que orquesta el análisis nocturno de los últimos 7 días de check-ins de atletas (#177 · KAN-472).
/// Identifica patrones de alarma (RPE alto sostenido, déficit de sueño, fatiga crítica) y emite avisos preventivos.
/// </summary>
public sealed class ProcessDailyOverloadAnalysisCommandHandler(
    IStructureAthleteClient structureAthleteClient,
    ISeguimientoClient seguimientoClient,
    IPreventiveAlertRepository alertRepository,
    IUnitOfWork unitOfWork,
    ILogger<ProcessDailyOverloadAnalysisCommandHandler> logger)
{
    public async Task<ProcessDailyOverloadAnalysisResult> HandleAsync(
        ProcessDailyOverloadAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        var targetDate = command.TargetDate ?? ArgentinaTime.Today;
        var fromDate = targetDate.AddDays(-6);
        var toDate = targetDate;

        logger.LogInformation(
            "Iniciando análisis diario de sobrecarga para la ventana {FromDate:yyyy-MM-dd} a {ToDate:yyyy-MM-dd}.",
            fromDate,
            toDate);

        // 1. Obtener los atletas activos desde Estructura
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

        // 3. Obtener alertas pendientes para no duplicar avisos no revisados del mismo tipo
        var pendingAlerts = await alertRepository.ListAsync(
            onlyUnreviewed: true,
            severity: null,
            cancellationToken: cancellationToken);

        var checkInsByAthlete = checkIns
            .GroupBy(c => c.AthleteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        int generatedAlerts = 0;
        int skippedAlerts = 0;

        foreach (var (athleteId, athlete) in athleteMap)
        {
            if (!checkInsByAthlete.TryGetValue(athleteId, out var athleteCheckIns) || athleteCheckIns.Count == 0)
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

            var patternResult = OverloadPatternDetector.Detect(athleteId, records);
            if (!patternResult.HasPattern)
            {
                continue;
            }

            // Evitar duplicados: si ya existe una alerta pendiente para este atleta y este tipo de alerta
            var alreadyPending = pendingAlerts.Any(a =>
                a.AthleteId == athleteId &&
                a.Type == patternResult.Type &&
                a.Status == AlertStatus.Pending);

            if (alreadyPending)
            {
                logger.LogInformation(
                    "Omitiendo alerta para el atleta {AthleteName} ({AthleteId}): ya existe una alerta pendiente de tipo {Type}.",
                    athlete.FullName,
                    athleteId,
                    patternResult.Type);
                skippedAlerts++;
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
            generatedAlerts++;

            logger.LogInformation(
                "Alerta preventiva generada para el atleta {AthleteName} ({AthleteId}): {Type} con severidad {Severity}.",
                athlete.FullName,
                athleteId,
                patternResult.Type,
                patternResult.Severity);
        }

        if (generatedAlerts > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Análisis nocturno completado. Atletas evaluados: {Athletes}, Alertas generadas: {Generated}, Omitidas por duplicado: {Skipped}.",
            athletes.Count,
            generatedAlerts,
            skippedAlerts);

        return new ProcessDailyOverloadAnalysisResult(athletes.Count, generatedAlerts, skippedAlerts);
    }
}

