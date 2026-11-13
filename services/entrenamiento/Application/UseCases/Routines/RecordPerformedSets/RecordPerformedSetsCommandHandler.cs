using System.Text.Json;
using Application.DTOs.Routines;
using Application.DTOs.TrainingSessions;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Services;

namespace Application.UseCases.Routines.RecordPerformedSets;

public class RecordPerformedSetsCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IStructureClient _structureClient;
    private readonly ITrainingSessionRepository _trainingSessionRepository;
    private readonly IAthleteRecordRepository _athleteRecordRepository;
    private readonly IPerformedSetRepository _performedSetRepository;
    private readonly IRecordProposalRepository _recordProposalRepository;
    private readonly IAthleteSessionAdjustmentRepository? _adjustmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RecordPerformedSetsCommandHandler(
        IStructureClient structureClient,
        ITrainingSessionRepository trainingSessionRepository,
        IAthleteRecordRepository athleteRecordRepository,
        IPerformedSetRepository performedSetRepository,
        IRecordProposalRepository recordProposalRepository,
        IUnitOfWork unitOfWork,
        IAthleteSessionAdjustmentRepository? adjustmentRepository = null)
    {
        _structureClient = structureClient;
        _trainingSessionRepository = trainingSessionRepository;
        _athleteRecordRepository = athleteRecordRepository;
        _performedSetRepository = performedSetRepository;
        _recordProposalRepository = recordProposalRepository;
        _unitOfWork = unitOfWork;
        _adjustmentRepository = adjustmentRepository;
    }

    public async Task<RecordPerformedSetsResponseDto> Handle(RecordPerformedSetsCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Sets == null || command.Sets.Count == 0)
        {
            throw new DomainException("Debe enviar al menos una serie para registrar.");
        }

        // Obtener el atleta autenticado
        var athlete = await _structureClient.GetMeAsync(cancellationToken)
            ?? throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");

        var athleteId = athlete.AthleteId;
        var categoryIds = athlete.Categories.Select(c => c.Id).ToList();
        var today = ArgentinaTime.Today;

        // Obtener la rutina del día
        var session = await _trainingSessionRepository.GetByAthleteIdAsync(athleteId, categoryIds, today, cancellationToken);
        if (session == null)
        {
            throw new DomainException("El atleta no tiene una sesión programada para el día de hoy.");
        }

        // Validar que los ejercicios pertenezcan a la sesión de hoy (considerando ajustes aprobados por el PF, KAN-452)
        var validExerciseIds = session.PrescribedExercises.Select(p => p.ExerciseId).ToHashSet();

        if (_adjustmentRepository != null)
        {
            var adjustment = await _adjustmentRepository.GetBySessionAthleteAndDateAsNoTrackingAsync(
                session.Id,
                athleteId,
                today,
                cancellationToken);

            if (adjustment != null && !string.IsNullOrWhiteSpace(adjustment.Items))
            {
                try
                {
                    var items = JsonSerializer.Deserialize<List<AthleteSessionAdjustmentItemDto>>(adjustment.Items, JsonOptions) ?? [];
                    foreach (var item in items)
                    {
                        if (item.SubstituteExerciseId != Guid.Empty)
                        {
                            validExerciseIds.Add(item.SubstituteExerciseId);
                        }
                    }
                }
                catch
                {
                    // Ignorar si no se puede deserializar
                }
            }
        }

        foreach (var set in command.Sets)
        {
            if (!validExerciseIds.Contains(set.ExerciseId))
            {
                throw new DomainException("Uno o más ejercicios no pertenecen a la sesión de hoy.");
            }
        }

        var performedSets = new List<PerformedSet>();
        int proposalsCount = 0;

        foreach (var item in command.Sets)
        {
            var weight = item.Weight ?? 0m;
            var reps = item.Reps ?? 0;    

            var performedSet = PerformedSet.Create(
                athleteId: athleteId,
                sessionId: session.Id,
                exerciseId: item.ExerciseId,
                date: today,
                weight: weight,
                reps: reps,
                timeSeconds: item.TimeSeconds,
                setNumber: item.SetNumber);

            performedSets.Add(performedSet);

            // Epley solo aplica a ejercicios de sobrecarga/fuerza (no a carreras/tiempos)
            if(!item.TimeSeconds.HasValue && weight > 0 && reps > 0){
                // Algoritmo Epley y evaluación de propuesta
                var proposed1Rm = EpleyCalculator.CalculateConservative1Rm(weight, reps);
                if (proposed1Rm.HasValue)
                {
                    var activeRecord = await _athleteRecordRepository.GetActiveRecordAsync(athleteId, item.ExerciseId, cancellationToken);

                    // Si no tiene récord vigente, no se propone nada
                    if (activeRecord != null && proposed1Rm.Value > activeRecord.Weight)
                    {
                        var existingPending = await _recordProposalRepository.GetPendingAsync(athleteId, item.ExerciseId, cancellationToken);
                        if (existingPending != null)
                        {
                            // Se reemplaza solo si la nueva es mayor
                            if (proposed1Rm.Value > existingPending.ProposedWeight)
                            {
                                existingPending.UpdateProposal(proposed1Rm.Value, weight, reps);
                                await _recordProposalRepository.UpdateAsync(existingPending, cancellationToken);
                                proposalsCount++;
                            }
                        }
                        else
                        {
                            var proposal = RecordProposal.Create(
                                athleteId: athleteId,
                                exerciseId: item.ExerciseId,
                                currentWeight: activeRecord.Weight,
                                proposedWeight: proposed1Rm.Value,
                                performedWeight: weight,
                                performedReps: reps);

                            await _recordProposalRepository.AddAsync(proposal, cancellationToken);
                            proposalsCount++;
                        }
                    }
                }
            }
        }

        await _performedSetRepository.AddRangeAsync(performedSets, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RecordPerformedSetsResponseDto
        {
            RecordedSetsCount = performedSets.Count,
            ProposalsGeneratedCount = proposalsCount
        };
    }
}
