using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.AthleteRecord.GetRecordsReliability;

// Devuelve, por categoría, cómo se distribuyen los atletas entre los 4 grupos de
// confiabilidad de sus récords vigentes. Un récord con origen Epley cuenta como
// Testeado (KAN-416).
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

        // Acumulador por grupo.
        var grouped = GroupOrder.ToDictionary(
            g => g,
            _ => new List<RecordReliabilityAthleteDto>()
        );

        foreach (var (athleteId, name) in athletes)
        {
            var records = await _recordRepository.ListAsync(athleteId, currentOnly: true, cancellationToken);

            // Si se filtra por ejercicio, solo se consideran los récords de ese ejercicio.
            var relevant = query.ExerciseId.HasValue
                ? records.Where(r => r.ExerciseId == query.ExerciseId.Value).ToList()
                : records;

            var group = ResolveGroup(relevant);
            grouped[group].Add(new RecordReliabilityAthleteDto(athleteId, name));
        }

        var groups = GroupOrder.Select(group => new RecordReliabilityGroupDto(
            group,
            grouped[group].Count,
            totalAthletes > 0 ? grouped[group].Count * 100.0 / totalAthletes : 0.0,
            grouped[group]
        )).ToList();

        return new RecordReliabilitySummaryDto(
            query.CategoryId,
            query.ExerciseId,
            totalAthletes,
            groups
        );
    }

    // Devuelve el grupo del récord vigente más confiable del atleta.
    private static string ResolveGroup(IReadOnlyList<Domain.Entities.AthleteRecord> records)
    {
        if (records.Count == 0)
            return "NoData";

        // Menor rango = más confiable.
        int Rank(RecordSource source) => source switch
        {
            RecordSource.Tested => 0,
            RecordSource.Epley => 0, // un récord Epley cuenta como Testeado
            RecordSource.Reported => 1,
            RecordSource.Estimated => 2,
            _ => 3,
        };

        var best = records
            .OrderBy(r => Rank(r.Source))
            .FirstOrDefault();

        return best is null ? "NoData" : best.Source switch
        {
            RecordSource.Tested => "Tested",
            RecordSource.Epley => "Tested",
            RecordSource.Reported => "Reported",
            RecordSource.Estimated => "Estimated",
            _ => "NoData",
        };
    }
}