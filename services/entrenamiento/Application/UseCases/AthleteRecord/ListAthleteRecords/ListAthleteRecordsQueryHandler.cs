using Application.DTOs.AthleteRecords;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.AthleteRecord.ListAthleteRecords;

public class ListAthleteRecordsQueryHandler
{
    private readonly IAthleteRecordRepository _repository;

    public ListAthleteRecordsQueryHandler(IAthleteRecordRepository repository)
    {
        _repository = repository;
    }

    public ListAthleteRecordsQueryHandler(IAthleteRecordRepository repository, IStructureClient structureClient)
        : this(repository)
    {
    }

    public async Task<List<AthleteRecordDto>> Handle(ListAthleteRecordsQuery query, CancellationToken cancellationToken = default)
    {
        var currentOnly = query.CurrentOnly ?? (!query.ExerciseId.HasValue);

        var records = await _repository.ListAsync(
            query.AthleteId,
            currentOnly,
            query.ExerciseId,
            query.From,
            query.To,
            cancellationToken);

        return records
            .Select(r => AthleteRecordMapper.ToDto(r, r.Exercise?.Name ?? string.Empty))
            .ToList();
    }
}
