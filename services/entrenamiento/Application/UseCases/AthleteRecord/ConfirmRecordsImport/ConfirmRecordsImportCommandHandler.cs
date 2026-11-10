using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.AthleteRecord.ConfirmRecordsImport;

public class ConfirmRecordsImportCommandHandler
{
    private readonly IAthleteRecordRepository _repository;
    private readonly IExerciseRepository _exerciseRepository;
    private readonly IStructureClient _structureClient;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmRecordsImportCommandHandler(
        IAthleteRecordRepository repository,
        IExerciseRepository exerciseRepository,
        IStructureClient structureClient,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _exerciseRepository = exerciseRepository;
        _structureClient = structureClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<List<AthleteRecordDto>> Handle(ConfirmRecordsImportCommand command, CancellationToken cancellationToken = default)
    {
        var rows = command.Request?.Rows;
        if (rows is null || rows.Count == 0)
            throw new DomainException("No hay filas para importar.");

        var created = new List<AthleteRecordDto>();

        var athletes = await _structureClient.ListAthletesAsync(cancellationToken);
        var athleteIds = athletes.Select(athlete => athlete.Id).ToHashSet();

        var exercises = await _exerciseRepository.ListAllAsync(cancellationToken);
        var exerciseMap = exercises.ToDictionary(exercise => exercise.Id);

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var row in rows)
            {
                if (!athleteIds.Contains(row.AthleteId))
                    throw new NotFoundException($"No se encontró el atleta con ID '{row.AthleteId}'.");

                if (!exerciseMap.TryGetValue(row.ExerciseId, out var exercise))
                    throw new NotFoundException("El ejercicio especificado no existe.");

                var exists = exercise.Name;

                var source = string.IsNullOrWhiteSpace(row.Origin)
                    ? RecordSource.Reported
                    : AthleteRecordMapper.ParseOrigin(row.Origin);

                var achievedAt = row.AchievedAt.HasValue
                    ? DateTime.SpecifyKind(row.AchievedAt.Value, DateTimeKind.Utc)
                    : DateTime.UtcNow;

                var newRecord = Domain.Entities.AthleteRecord.Create(
                    row.AthleteId,
                    row.ExerciseId,
                    row.WeightOneRepMaxKg,
                    source,
                    achievedAt);

                var previousRecord = await _repository.GetActiveRecordAsync(row.AthleteId, row.ExerciseId, cancellationToken);

                if (previousRecord is not null
                    && previousRecord.Weight == row.WeightOneRepMaxKg
                    && previousRecord.Date == achievedAt)
                {
                    continue;
                }

                if (previousRecord is not null)
                {
                    previousRecord.Deactivate();
                    await _repository.UpdateAsync(previousRecord, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await _repository.AddAsync(newRecord, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                created.Add(AthleteRecordMapper.ToDto(newRecord, exists));
            }
        }, cancellationToken);

        return created;
    }
}