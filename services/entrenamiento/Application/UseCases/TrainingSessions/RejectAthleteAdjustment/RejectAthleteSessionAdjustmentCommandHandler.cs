using System.Text.Json;
using Application.DTOs.TrainingSessions;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.TrainingSessions.RejectAthleteAdjustment;

public sealed class RejectAthleteSessionAdjustmentCommandHandler(
    ITrainingSessionRepository trainingSessionRepository,
    IAthleteSessionAdjustmentRepository adjustmentRepository,
    IStructureClient structureClient,
    IUnitOfWork unitOfWork,
    IAthleteVariantRepository? variantRepository = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AthleteSessionAdjustmentResponseDto> Handle(
        RejectAthleteSessionAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.SessionId == Guid.Empty)
            throw new NotFoundException("La sesión de entrenamiento no existe.");

        var request = command.Request ?? throw new DomainException("La solicitud de rechazo es obligatoria.");

        if (request.AthleteId == Guid.Empty)
            throw new NotFoundException("El atleta no existe o no es tuyo.");

        var targetDate = request.Date ?? ArgentinaTime.Today;
        if (targetDate == default)
            throw new DomainException("La fecha del rechazo es obligatoria.");

        // 1. Verificar que la sesión exista y pertenezca al PF del token.
        var session = await trainingSessionRepository.GetByIdAsync(command.SessionId, cancellationToken)
            ?? throw new NotFoundException("La sesión de entrenamiento no existe.");

        // 2. Verificar que el atleta exista y pertenezca al PF del token.
        var athlete = await structureClient.GetAthleteByIdAsync(request.AthleteId, cancellationToken)
            ?? throw new NotFoundException("El atleta no existe o no es tuyo.");

        // 3. Resolver y validar vínculo con la planificación vigente.
        var planIdFromSession = await trainingSessionRepository.GetPlanIdBySessionIdAsync(session.Id, cancellationToken);
        var sessionPlanId = planIdFromSession ?? session.Week?.TrainingPlanId ?? Guid.Empty;
        var trainingPlanId = request.TrainingPlanId ?? (sessionPlanId != Guid.Empty ? sessionPlanId : Guid.Empty);

        if (request.TrainingPlanId.HasValue &&
            request.TrainingPlanId.Value != Guid.Empty &&
            sessionPlanId != Guid.Empty &&
            request.TrainingPlanId.Value != sessionPlanId)
        {
            throw new DomainException("La sesión indicada no pertenece al plan de entrenamiento especificado.");
        }

        var existing = await adjustmentRepository.GetBySessionAthleteAndDateAsync(
            session.Id,
            athlete.Id,
            targetDate,
            cancellationToken);

        // 4. Desactivar variantes activas previas de los ejercicios afectados para restaurar la rutina base.
        if (variantRepository is not null)
        {
            var originalExerciseIds = new HashSet<Guid>();

            if (existing is not null && !string.IsNullOrWhiteSpace(existing.Items))
            {
                try
                {
                    var previousItems = JsonSerializer.Deserialize<List<AthleteSessionAdjustmentItemDto>>(existing.Items, JsonOptions);
                    if (previousItems is not null)
                    {
                        foreach (var item in previousItems)
                        {
                            if (item.OriginalExerciseId != Guid.Empty)
                                originalExerciseIds.Add(item.OriginalExerciseId);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Si los ítems previos no se pueden deserializar, continuamos con los ejercicios prescriptos de la sesión.
                }
            }

            foreach (var prescribed in session.PrescribedExercises)
            {
                if (prescribed.ExerciseId != Guid.Empty)
                    originalExerciseIds.Add(prescribed.ExerciseId);
            }

            foreach (var originalExerciseId in originalExerciseIds)
            {
                var activeVariant = await variantRepository.GetActiveByAthleteAndOriginalExerciseAsync(
                    athlete.Id,
                    originalExerciseId,
                    targetDate,
                    cancellationToken);

                if (activeVariant is null)
                    continue;

                var endDate = targetDate > activeVariant.ValidFrom
                    ? targetDate.AddDays(-1)
                    : activeVariant.ValidFrom;
                activeVariant.Deactivate(endDate);
            }
        }

        AthleteSessionAdjustment adjustment;
        if (existing is not null)
        {
            existing.MarkRejected(request.Note, trainingPlanId);
            adjustment = existing;
        }
        else
        {
            adjustment = AthleteSessionAdjustment.CreateRejected(
                trainingSessionId: session.Id,
                athleteId: athlete.Id,
                date: targetDate,
                note: request.Note,
                trainingPlanId: trainingPlanId);

            await adjustmentRepository.AddAsync(adjustment, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AthleteSessionAdjustmentResponseDto(
            Id: adjustment.Id,
            SessionId: adjustment.TrainingSessionId,
            AthleteId: adjustment.AthleteId,
            Date: adjustment.Date,
            Note: adjustment.Note,
            Items: [],
            TrainingPlanId: adjustment.TrainingPlanId != Guid.Empty ? adjustment.TrainingPlanId : null,
            LoadReductionPercentage: null,
            Status: adjustment.Status);
    }
}
