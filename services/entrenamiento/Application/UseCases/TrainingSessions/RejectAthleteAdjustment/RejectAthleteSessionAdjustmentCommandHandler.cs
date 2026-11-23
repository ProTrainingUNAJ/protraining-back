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

        var preservedItems = existing is not null
            ? DeserializeItems(existing.Items)
            : [];

        // 4. Desactivar únicamente las variantes creadas por el ajuste previo que se rechaza (KAN-636).
        // Si no había ajuste previo aprobado ese día, no se desactiva ninguna variante del atleta.
        if (variantRepository is not null &&
            existing is not null &&
            !existing.IsRejected &&
            preservedItems.Count > 0)
        {
            var distinctPairs = new HashSet<(Guid OriginalExerciseId, Guid SubstituteExerciseId)>();
            foreach (var item in preservedItems)
            {
                if (item.OriginalExerciseId == Guid.Empty ||
                    item.SubstituteExerciseId == Guid.Empty ||
                    item.OriginalExerciseId == item.SubstituteExerciseId)
                {
                    continue;
                }

                distinctPairs.Add((item.OriginalExerciseId, item.SubstituteExerciseId));
            }

            foreach (var (originalExerciseId, substituteExerciseId) in distinctPairs)
            {
                var activeVariant = await variantRepository.GetActiveByAthleteAndOriginalExerciseAsync(
                    athlete.Id,
                    originalExerciseId,
                    targetDate,
                    cancellationToken);

                if (activeVariant is null)
                    continue;

                if (activeVariant.ValidFrom != targetDate ||
                    activeVariant.SubstituteExerciseId != substituteExerciseId)
                {
                    continue;
                }

                activeVariant.Deactivate(targetDate);
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
                rejectionReason: request.Note,
                trainingPlanId: trainingPlanId);

            await adjustmentRepository.AddAsync(adjustment, cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            throw new ConflictException(
                "Ya existe una decisión registrada simultáneamente para esta sesión, atleta y fecha. Recargá e intentá de nuevo.");
        }

        return new AthleteSessionAdjustmentResponseDto(
            Id: adjustment.Id,
            SessionId: adjustment.TrainingSessionId,
            AthleteId: adjustment.AthleteId,
            Date: adjustment.Date,
            Note: adjustment.Note,
            Items: preservedItems,
            TrainingPlanId: adjustment.TrainingPlanId != Guid.Empty ? adjustment.TrainingPlanId : null,
            LoadReductionPercentage: adjustment.LoadReductionPercentage,
            Status: adjustment.Status,
            RejectionReason: adjustment.RejectionReason);
    }

    private static List<AthleteSessionAdjustmentItemDto> DeserializeItems(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<AthleteSessionAdjustmentItemDto>>(itemsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
