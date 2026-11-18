using Application.DTOs.Alerts;
using Application.DTOs.DayAdjustments;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Alerts.ApplyAlertAdjustment;

public sealed record ApplyAlertAdjustmentCommand(Guid AlertId);

/// <summary>
/// Aplica el ajuste sugerido por un aviso preventivo en Entrenamiento con un solo clic (#183 · KAN-478).
/// Crea la variante de sesión en Entrenamiento y marca el aviso como Aplicado.
/// Si Entrenamiento falla, la excepción no se enmascara y el aviso permanece en su estado anterior.
/// </summary>
public sealed class ApplyAlertAdjustmentCommandHandler(
    IPreventiveAlertRepository repository,
    ITrainingPlanServiceClient trainingPlanServiceClient,
    IUnitOfWork unitOfWork)
{
    public async Task<ApplyAlertAdjustmentResponseDto> HandleAsync(
        ApplyAlertAdjustmentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.AlertId == Guid.Empty)
            throw new DomainException("El identificador del aviso es obligatorio.");

        var alert = await repository.GetByIdAsync(command.AlertId, cancellationToken)
            ?? throw new NotFoundException("El aviso solicitado no fue encontrado.");

        if (alert.Status == AlertStatus.Applied)
        {
            return new ApplyAlertAdjustmentResponseDto(
                Message: "El aviso ya se encuentra aplicado.",
                AlertId: alert.Id,
                Status: alert.Status.ToString());
        }

        // 1. Obtener la rutina del día del atleta desde Entrenamiento
        var todayRoutine = await trainingPlanServiceClient.GetTodayRoutineAsync(alert.AthleteId, cancellationToken);
        if (todayRoutine is null || !todayRoutine.SessionId.HasValue || todayRoutine.SessionId.Value == Guid.Empty)
        {
            throw new DomainException("El atleta no tiene una sesión de entrenamiento programada para el día de hoy.");
        }

        // 2. Construir la solicitud de ajuste remoto para Entrenamiento
        var reductionPercentage = alert.SuggestedAdjustmentPercentage ?? 20;
        var note = !string.IsNullOrWhiteSpace(alert.Suggestion)
            ? alert.Suggestion
            : $"Ajuste preventivo aplicado con 1 clic: {alert.Reason}";

        var remoteRequest = new ApplyAthleteAdjustmentRequestRemoteDto(
            AthleteId: alert.AthleteId,
            Date: ArgentinaTime.Today,
            Note: note,
            Items: [],
            LoadReductionPercentage: reductionPercentage);

        // 3. Aplicar en Entrenamiento. Si Entrenamiento falla (error 500/timeout), la excepción
        // escala y corta el flujo, garantizando que el aviso NO quede marcado como aplicado.
        await trainingPlanServiceClient.ApplyAthleteSessionAdjustmentAsync(
            todayRoutine.SessionId.Value,
            remoteRequest,
            cancellationToken);

        // 4. Marcar como aplicado y persistir en la base de datos de Asistente
        alert.MarkAsApplied(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ApplyAlertAdjustmentResponseDto(
            Message: "Ajuste preventivo aplicado exitosamente en Entrenamiento.",
            AlertId: alert.Id,
            Status: alert.Status.ToString());
    }
}
