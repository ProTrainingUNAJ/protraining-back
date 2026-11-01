using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.AthleteRecord.CompleteOnboarding;

public class CompleteOnboardingCommandHandler
{
    private readonly IAthleteRecordRepository _repository;
    private readonly IStructureClient _structureClient;
    private readonly IUnitOfWork _unitOfWork;

    public CompleteOnboardingCommandHandler(
        IAthleteRecordRepository repository,
        IStructureClient structureClient,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _structureClient = structureClient;
        _unitOfWork = unitOfWork;
    }

    public Task<List<AthleteRecordDto>> HandleAsync(CompleteOnboardingCommand command, CancellationToken cancellationToken = default)
        => Handle(command, cancellationToken);

    public async Task<List<AthleteRecordDto>> Handle(CompleteOnboardingCommand command, CancellationToken cancellationToken = default)
    {
        var records = command.Records ?? new List<OnboardingRecordRequest>();
        if (records.Count == 0)
        {
            throw new DomainException("Debe declarar al menos una marca física para completar el onboarding.");
        }

        // El atleta sale del token (rol Atleta), igual que en RecordPerformedSets.
        var athlete = await _structureClient.GetMeAsync(cancellationToken)
            ?? throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");

        var athleteId = athlete.AthleteId;
        var created = new List<AthleteRecordDto>();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var item in records)
            {
                if (item.WeightOneRepMaxKg <= 0)
                {
                    throw new DomainException("El peso de la marca debe ser mayor a 0 kg.");
                }

                var exerciseName = await _repository.GetExerciseNameAsync(item.ExerciseId, cancellationToken)
                    ?? throw new NotFoundException("El ejercicio especificado no existe.");

                var previousRecord = await _repository.GetActiveRecordAsync(athleteId, item.ExerciseId, cancellationToken);
                if (previousRecord != null)
                {
                    previousRecord.Deactivate();
                    await _repository.UpdateAsync(previousRecord, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                var newRecord = Domain.Entities.AthleteRecord.Create(
                    athleteId,
                    item.ExerciseId,
                    item.WeightOneRepMaxKg,
                    RecordSource.Estimated,
                    DateTime.UtcNow);

                await _repository.AddAsync(newRecord, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                created.Add(AthleteRecordMapper.ToDto(newRecord, exerciseName));
            }
        }, cancellationToken);

        return created;
    }
}