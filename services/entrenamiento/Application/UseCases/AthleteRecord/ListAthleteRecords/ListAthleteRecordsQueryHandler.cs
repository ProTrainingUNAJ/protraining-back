using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.AthleteRecord.ListAthleteRecords;

public class ListAthleteRecordsQueryHandler
{
    private readonly IAthleteRecordRepository _repository;
    private readonly IStructureClient _structureClient;

    public ListAthleteRecordsQueryHandler(IAthleteRecordRepository repository, IStructureClient structureClient)
    {
        _repository = repository;
        _structureClient = structureClient;
    }

    public async Task<List<AthleteRecordDto>> Handle(ListAthleteRecordsQuery query, CancellationToken cancellationToken = default)
    {
        // Estructura responde 404 si el atleta no es de este PF.
        var athlete = await _structureClient.GetAthleteByIdAsync(query.AthleteId, cancellationToken);
        if (athlete == null)
            throw new NotFoundException($"No se encontró el atleta con ID '{query.AthleteId}'.");

        var records = await _repository.ListAsync(query.AthleteId, query.CurrentOnly, cancellationToken);

        return records
            .Select(r => AthleteRecordMapper.ToDto(r, r.Exercise?.Name ?? string.Empty))
            .ToList();
    }
}
