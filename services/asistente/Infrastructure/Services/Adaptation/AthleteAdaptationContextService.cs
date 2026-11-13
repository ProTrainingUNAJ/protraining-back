using Application.DTOs.Adaptation;
using Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Adaptation;

public sealed class AthleteAdaptationContextService(
    ISeguimientoClient seguimientoClient,
    ITrainingPlanServiceClient trainingClient,
    ILogger<AthleteAdaptationContextService> logger) : IAthleteAdaptationContextService
{
    public async Task<AthleteAdaptationContextDto> GetAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        if (athleteId == Guid.Empty)
            throw new ArgumentException("El identificador del atleta no puede ser vacío.", nameof(athleteId));

        var injuriesTask = seguimientoClient.GetActiveInjuriesAsync(
            athleteId,
            cancellationToken);
        var recordsTask = trainingClient.GetCurrentRecordsAsync(
            athleteId,
            cancellationToken);

        await Task.WhenAll(injuriesTask, recordsTask);

        var injuries = (await injuriesTask)
            .Where(injury => string.Equals(injury.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var records = (await recordsTask)
            .Where(record => record.IsCurrent)
            .ToList();

        var painZones = string.Join(
            ",",
            injuries
                .Select(injury => injury.Zone?.Trim())
                .Where(zone => !string.IsNullOrWhiteSpace(zone))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(zone => $"{zone}:3"));

        var exerciseIds = records
            .Select(record => record.ExerciseId)
            .Where(exerciseId => exerciseId != Guid.Empty)
            .Distinct()
            .ToList();

        var restrictions = string.IsNullOrWhiteSpace(painZones) || exerciseIds.Count == 0
            ? []
            : await trainingClient.CheckExerciseRestrictionsAsync(
                painZones,
                exerciseIds,
                cancellationToken);

        var result = restrictions
            .Where(restriction => restriction.Restricted)
            .Select(restriction => new ExerciseRestrictionContextDto(
                restriction.ExerciseId,
                restriction.Zone,
                restriction.Reason,
                (restriction.Substitutes ?? [])
                    .Where(substitute => substitute.ExerciseId != Guid.Empty)
                    .Select(substitute => new AuthorizedSubstituteDto(
                        substitute.ExerciseId,
                        substitute.Name))
                    .ToList()))
            .ToList();

        logger.LogInformation(
            "Contexto de adaptación construido para el atleta {AthleteId}: {InjuryCount} lesiones activas y {RecordCount} récords vigentes.",
            athleteId,
            injuries.Count,
            records.Count);

        return new AthleteAdaptationContextDto(athleteId, injuries, records, result);
    }
}
