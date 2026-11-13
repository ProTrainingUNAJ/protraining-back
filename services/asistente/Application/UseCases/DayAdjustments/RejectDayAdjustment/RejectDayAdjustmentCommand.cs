using Application.Common.DayAdjustments;
using Application.DTOs.DayAdjustments;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.DayAdjustments.RejectDayAdjustment;

public sealed record RejectDayAdjustmentCommand(
    Guid Id,
    RejectDayAdjustmentRequestDto? Request = null
);

/// <summary>
/// Rechaza la propuesta de ajuste del día dejando la sesión sin cambios y registra
/// la decisión en la memoria del PF dentro de la misma transacción (#157 · KAN-452).
/// </summary>
public sealed class RejectDayAdjustmentCommandHandler(
    IDayAdjustmentRepository dayAdjustmentRepository,
    IPfDecisionRepository pfDecisionRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<DayAdjustmentResponseDto> HandleAsync(
        RejectDayAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Id == Guid.Empty)
            throw new DomainException("El identificador de la propuesta de ajuste es obligatorio.");

        var adjustment = await dayAdjustmentRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("La propuesta de ajuste del día no existe.");

        if (adjustment.Status != DayAdjustmentStatus.Pending)
            throw new ConflictException(adjustment.GetNonPendingStatusMessage());

        var reason = command.Request?.Reason;
        adjustment.Reject(reason);

        var items = DayAdjustmentMapper.DeserializeItems(adjustment.Items);
        var decisions = items
            .Select(item => PfDecision.Create(
                athleteId: adjustment.AthleteId,
                source: PfDecisionSource.DayAdjustment,
                sourceId: adjustment.Id,
                suggestedExerciseId: item.SuggestedSubstituteExerciseId,
                decision: PfDecisionType.Rejected,
                zone: item.Zone,
                reason: reason,
                date: adjustment.Date))
            .ToList();

        if (decisions.Count > 0)
        {
            await pfDecisionRepository.AddRangeAsync(decisions, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DayAdjustmentMapper.ToResponseDto(adjustment);
    }
}
