using Application.DTOs.AthleteRecords;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.AthleteRecord.CompleteOnboarding;

public class CompleteOnboardingCommandHandler
{
    private const decimal MinWeightKg = 1m;
    private const decimal MaxWeightKg = 500m;

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

    public async Task<List<AthleteRecordDto>> Handle(CompleteOnboardingCommand command, CancellationToken cancellationToken = default)
    {
        var records = command.Records ?? new List<OnboardingRecordRequest>();
        if (records.Count == 0)
        {
            throw new DomainException("Debe declarar al menos una marca física para completar el onboarding.");
        }

        // Se valida todo antes de escribir para no dejar marcas a medio guardar.
        Validate(records);

        // El atleta sale del token (rol Atleta), igual que en RecordPerformedSets.
        var athlete = await _structureClient.GetMeAsync(cancellationToken)
            ?? throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");

        var athleteId = athlete.AthleteId;

        // El onboarding nunca pisa una marca existente: solo crea las de ejercicios sin récord vigente.
        var toCreate = new List<(OnboardingRecordRequest Item, string ExerciseName)>();
        foreach (var item in records)
        {
            var exerciseName = await _repository.GetExerciseNameAsync(item.ExerciseId, cancellationToken)
                ?? throw new NotFoundException("El ejercicio especificado no existe.");

            var activeRecord = await _repository.GetActiveRecordAsync(athleteId, item.ExerciseId, cancellationToken);
            if (activeRecord != null)
            {
                continue;
            }

            toCreate.Add((item, exerciseName));
        }

        if (toCreate.Count == 0)
        {
            return new List<AthleteRecordDto>();
        }

        var newRecords = new List<(Domain.Entities.AthleteRecord Record, string ExerciseName)>();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var (item, exerciseName) in toCreate)
            {
                var newRecord = Domain.Entities.AthleteRecord.Create(
                    athleteId,
                    item.ExerciseId,
                    item.WeightOneRepMaxKg,
                    RecordSource.Estimated,
                    DateTime.UtcNow);

                await _repository.AddAsync(newRecord, cancellationToken);
                newRecords.Add((newRecord, exerciseName));
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return newRecords
            .Select(record => AthleteRecordMapper.ToDto(record.Record, record.ExerciseName))
            .ToList();
    }

    private static void Validate(List<OnboardingRecordRequest> records)
    {
        var seenExerciseIds = new HashSet<Guid>();
        foreach (var item in records)
        {
            if (item.ExerciseId == Guid.Empty)
            {
                throw new DomainException("El ejercicio es obligatorio.");
            }

            if (!seenExerciseIds.Add(item.ExerciseId))
            {
                throw new DomainException("No se puede declarar dos veces el mismo ejercicio.");
            }

            if (item.WeightOneRepMaxKg < MinWeightKg || item.WeightOneRepMaxKg > MaxWeightKg)
            {
                throw new DomainException($"El peso de la marca debe estar entre {MinWeightKg:0} y {MaxWeightKg:0} kg.");
            }
        }
    }
}
