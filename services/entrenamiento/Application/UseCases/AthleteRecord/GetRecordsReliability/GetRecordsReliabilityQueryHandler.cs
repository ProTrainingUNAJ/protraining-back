using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.AthleteRecord.GetRecordsReliability;

// Devuelve, por categoría, cuántos récords vigentes hay de cada grupo de
// confiabilidad y qué atletas los tienen. Un atleta puede aparecer en más de un
// grupo si tiene récords de distintos orígenes. Un récord con origen Epley cuenta
// como Testeado (KAN-416).
public sealed class GetRecordsReliabilityQueryHandler
{
    private readonly IStructureClient _structureClient;
    private readonly IAthleteRecordRepository _recordRepository;

    private static readonly string[] GroupOrder = ["Tested", "Reported", "Estimated", "NoData"];

    public GetRecordsReliabilityQueryHandler(
        IStructureClient structureClient,
        IAthleteRecordRepository recordRepository)
    {
        _structureClient = structureClient;
        _recordRepository = recordRepository;
    }

    public async Task<RecordReliabilitySummaryDto> Handle(
        GetRecordsReliabilityQuery query,
        CancellationToken cancellationToken = default)
    {
        // Categoría de otro PF (o inexistente) → 404, igual que un recurso propio.
        if (!await _structureClient.IsOwnCategoryAsync(query.CategoryId, cancellationToken))
            throw new NotFoundException("La categoría no existe o no es tuya.");

        var athleteIds = await _structureClient.GetCategoryAthletesAsync(query.CategoryId, cancellationToken);

        // Nombres de los atletas de la categoría.
        var athletes = new List<(Guid Id, string Name)>(athleteIds.Count);
        foreach (var athleteId in athleteIds)
        {
            var athlete = await _structureClient.GetAthleteByIdAsync(athleteId, cancellationToken);
            athletes.Add((athleteId, athlete?.FullName ?? "Sin nombre"));
        }

        var totalAthletes = athletes.Count;

        // Acumulador por grupo. Cada atleta puede sumarse a varios grupos.
        var grouped = GroupOrder.ToDictionary(
            g => g,
            _ => new List<RecordReliabilityAthleteDto>()
        );

        var totalRecords = 0;

        foreach (var (athleteId, name) in athletes)
        {
            var records = await _recordRepository.ListAsync(athleteId, currentOnly: true, cancellationToken);

            // Si se filtra por ejercicio, solo se consideran los récords de ese ejercicio.
            var relevant = (query.ExerciseId.HasValue
                    ? records.Where(r => r.ExerciseId == query.ExerciseId.Value)
                    : records)
                .Where(r => r.Source != RecordSource.NoData)
                .ToList();

            if (relevant.Count == 0)
            {
                grouped["NoData"].Add(new RecordReliabilityAthleteDto(athleteId, name, 0));
                continue;
            }

            totalRecords += relevant.Count;

            var countsByGroup = relevant
                .GroupBy(r => ResolveGroup(r.Source))
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var group in GroupOrder)
            {
                if (group == "NoData")
                    continue;

                if (countsByGroup.TryGetValue(group, out var recordCount) && recordCount > 0)
                    grouped[group].Add(new RecordReliabilityAthleteDto(athleteId, name, recordCount));
            }
        }

        var groups = GroupOrder.Select(group =>
        {
            var groupAthletes = grouped[group];
            var recordCount = groupAthletes.Sum(a => a.RecordCount);
            var percentage = group == "NoData"
                ? totalAthletes > 0 ? groupAthletes.Count * 100.0 / totalAthletes : 0.0
                : totalRecords > 0 ? recordCount * 100.0 / totalRecords : 0.0;

            return new RecordReliabilityGroupDto(
                group,
                groupAthletes.Count,
                recordCount,
                percentage,
                groupAthletes
            );
        }).ToList();

        return new RecordReliabilitySummaryDto(
            query.CategoryId,
            query.ExerciseId,
            totalAthletes,
            totalRecords,
            groups
        );
    }

    // Devuelve el grupo de confiabilidad del origen de un récord.
    private static string ResolveGroup(RecordSource source) => source switch
    {
        RecordSource.Tested => "Tested",
        RecordSource.Epley => "Tested", // un récord Epley cuenta como Testeado
        RecordSource.Reported => "Reported",
        RecordSource.Estimated => "Estimated",
        _ => "NoData",
    };
}
