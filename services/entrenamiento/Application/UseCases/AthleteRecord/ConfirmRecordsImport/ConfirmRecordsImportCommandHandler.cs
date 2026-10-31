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
    private readonly IStructureClient _structureClient;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmRecordsImportCommandHandler(
        IAthleteRecordRepository repository,
        IStructureClient structureClient,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _structureClient = structureClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<List<AthleteRecordDto>> Handle(ConfirmRecordsImportCommand command, CancellationToken cancellationToken = default)
    {
        var rows = command.Request?.Rows;
        if (rows is null || rows.Count == 0)
            throw new DomainException("No hay filas para importar.");

        var created = new List<AthleteRecordDto>();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var row in rows)
            {
                var athlete = await _structureClient.GetAthleteByIdAsync(row.AthleteId, cancellationToken);
                if (athlete is null)
                    throw new NotFoundException($"No se encontró el atleta con ID '{row.AthleteId}'.");

                var exists = await _repository.GetExerciseNameAsync(row.ExerciseId, cancellationToken);
                if (exists is null)
                    throw new NotFoundException("El ejercicio especificado no existe.");

                var source = string.IsNullOrWhiteSpace(row.Origin)
                    ? RecordSource.Reported
                    : AthleteRecordMapper.ParseOrigin(row.Origin);

                var newRecord = Domain.Entities.AthleteRecord.Create(
                    row.AthleteId,
                    row.ExerciseId,
                    row.WeightOneRepMaxKg,
                    source,
                    row.AchievedAt ?? DateTime.UtcNow);

                var previousRecord = await _repository.GetActiveRecordAsync(row.AthleteId, row.ExerciseId, cancellationToken);
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
