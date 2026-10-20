using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Application.UseCases.AthleteRecord.RegisterAthleteRecord;

public class RegisterAthleteRecordCommandHandler
{
    private readonly IAthleteRecordRepository _repository;
    private readonly IStructureClient _structureClient;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterAthleteRecordCommandHandler(IAthleteRecordRepository repository, IStructureClient structureClient, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _structureClient = structureClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<AthleteRecordDto> Handle(RegisterAthleteRecordCommand command, CancellationToken cancellationToken = default)
    {
        var request = command.Request;
        var source = AthleteRecordMapper.ParseOrigin(request.Origin);

        // Estructura responde 404 si el atleta no es de este PF.
        var athlete = await _structureClient.GetAthleteByIdAsync(command.AthleteId, cancellationToken);
        if (athlete == null)
            throw new NotFoundException($"No se encontró el atleta con ID '{command.AthleteId}'.");

        var exerciseName = await _repository.GetExerciseNameAsync(request.ExerciseId, cancellationToken)
            ?? throw new NotFoundException("El ejercicio especificado no existe.");

        var newRecord = Domain.Entities.AthleteRecord.Create(
            command.AthleteId,
            request.ExerciseId,
            request.WeightOneRepMaxKg,
            source,
            DateTime.UtcNow
        );

        // Solo puede haber un récord vigente por atleta y ejercicio (índice único en la base):
        // primero se guarda el anterior como no vigente y después el nuevo, en la misma transacción.
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var previousRecord = await _repository.GetActiveRecordAsync(command.AthleteId, request.ExerciseId, cancellationToken);
            if (previousRecord != null)
            {
                previousRecord.Deactivate();
                await _repository.UpdateAsync(previousRecord, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _repository.AddAsync(newRecord, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return AthleteRecordMapper.ToDto(newRecord, exerciseName);
    }
}
