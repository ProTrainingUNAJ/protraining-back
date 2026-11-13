using Application.Common.DayAdjustments;
using Application.DTOs.DayAdjustments;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.DayAdjustments.ApproveDayAdjustment;

public sealed record ApproveDayAdjustmentCommand(
    Guid Id,
    ApproveDayAdjustmentRequestDto? Request = null
);

/// <summary>
/// Aprueba la propuesta de ajuste del día (permitiendo cambiar el sustituto por otro de la lista de KAN-387),
/// aplica el ajuste en Entrenamiento y registra la decisión en la memoria del PF dentro de la misma transacción (#157 · KAN-452).
/// </summary>
public sealed class ApproveDayAdjustmentCommandHandler(
    IDayAdjustmentRepository dayAdjustmentRepository,
    IPfDecisionRepository pfDecisionRepository,
    ITrainingPlanServiceClient trainingPlanServiceClient,
    IUnitOfWork unitOfWork)
{
    public async Task<DayAdjustmentResponseDto> HandleAsync(
        ApproveDayAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Id == Guid.Empty)
            throw new DomainException("El identificador de la propuesta de ajuste es obligatorio.");

        var adjustment = await dayAdjustmentRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("La propuesta de ajuste del día no existe.");

        if (adjustment.Status != DayAdjustmentStatus.Pending)
            throw new ConflictException(adjustment.GetNonPendingStatusMessage());

        var items = DayAdjustmentMapper.DeserializeItems(adjustment.Items);
        var anyModified = false;

        if (command.Request?.Items is { Count: > 0 } overrides)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var currentItem = items[i];
                var overrideItem = overrides.FirstOrDefault(o => o.OriginalExerciseId == currentItem.OriginalExerciseId);
                if (overrideItem is null)
                    continue;

                var validOption = currentItem.AvailableSubstitutes
                    .FirstOrDefault(s => s.ExerciseId == overrideItem.SubstituteExerciseId);

                if (validOption is null)
                {
                    throw new DomainException(
                        "El ejercicio sustituto elegido no pertenece a la lista de sustitutos habilitados para esa zona de dolor.");
                }

                if (currentItem.SubstituteExerciseId != validOption.ExerciseId)
                {
                    anyModified = true;
                    items[i] = currentItem with
                    {
                        SubstituteExerciseId = validOption.ExerciseId,
                        SubstituteExerciseName = validOption.Name
                    };
                }
            }

            // Verificar si se envió un ejercicio original que no existe en la propuesta
            foreach (var overrideItem in overrides)
            {
                if (items.All(it => it.OriginalExerciseId != overrideItem.OriginalExerciseId))
                {
                    throw new DomainException(
                        "El ejercicio original indicado no forma parte de la propuesta de ajuste.");
                }
            }
        }

        var updatedJustification = anyModified
            ? DayAdjustmentMapper.BuildDeterministicJustification(items)
            : adjustment.Justification;

        var remoteRequest = new ApplyAthleteAdjustmentRequestRemoteDto(
            AthleteId: adjustment.AthleteId,
            Date: adjustment.Date,
            Note: updatedJustification,
            Items: items
                .Select(i => new ApplyAthleteAdjustmentItemRemoteDto(i.OriginalExerciseId, i.SubstituteExerciseId))
                .ToList());

        // Nota sobre consistencia entre servicios (#157 · KAN-452):
        // Primero se aplica el ajuste en Entrenamiento y posteriormente se persiste el estado
        // Approved y las decisiones del PF en Asistente. Si el guardado en Asistente llega a fallar,
        // Entrenamiento ya tiene registrado el ajuste mientras que la propuesta continúa en Pending.
        // Dado que el endpoint de Entrenamiento (ApplyAthleteSessionAdjustment) actúa como un upsert
        // idempotente (actualiza el registro existente en lugar de duplicarlo), un reintento posterior
        // por parte del PF es completamente seguro y no corrompe los datos.
        await trainingPlanServiceClient.ApplyAthleteSessionAdjustmentAsync(
            adjustment.SessionId,
            remoteRequest,
            cancellationToken);

        adjustment.Approve(DayAdjustmentMapper.SerializeItems(items), updatedJustification);

        var decisions = new List<PfDecision>(items.Count);
        foreach (var item in items)
        {
            var isModified = item.SubstituteExerciseId != item.SuggestedSubstituteExerciseId;
            var decisionType = isModified ? PfDecisionType.Modified : PfDecisionType.Approved;

            decisions.Add(PfDecision.Create(
                athleteId: adjustment.AthleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: adjustment.Id,
                suggestedExerciseId: item.SuggestedSubstituteExerciseId,
                decision: decisionType,
                finalExerciseId: item.SubstituteExerciseId,
                zone: item.Zone,
                date: adjustment.Date));
        }

        await pfDecisionRepository.AddRangeAsync(decisions, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DayAdjustmentMapper.ToResponseDto(adjustment);
    }
}
