using Application.DTOs.Alerts;
using Application.DTOs.DayAdjustments;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Common;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.Alerts.ApplyAlertAdjustment;

/// <param name="NextSession">
/// Si es true, el ajuste va a la próxima sesión del atleta (los 7 días siguientes) en lugar de la de hoy:
/// sirve cuando el aviso llega un día que no entrena (ensayo de la demo, KAN-481).
/// </param>
public sealed record ApplyAlertAdjustmentCommand(Guid AlertId, bool NextSession = false);

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

        // 1. Obtener la sesión a ajustar desde Entrenamiento: la de hoy o la próxima
        var (sessionId, sessionDate) = command.NextSession
            ? await FindNextSessionAsync(alert.AthleteId, cancellationToken)
            : await FindTodaySessionAsync(alert.AthleteId, cancellationToken);

        // 2. Construir la solicitud de ajuste remoto para Entrenamiento
        var reductionPercentage = alert.SuggestedAdjustmentPercentage ?? 20;
        var note = !string.IsNullOrWhiteSpace(alert.Suggestion)
            ? alert.Suggestion
            : $"Ajuste preventivo aplicado con 1 clic: {alert.Reason}";

        var remoteRequest = new ApplyAthleteAdjustmentRequestRemoteDto(
            AthleteId: alert.AthleteId,
            Date: sessionDate,
            Note: note,
            Items: [],
            LoadReductionPercentage: reductionPercentage);

        // 3. Aplicar en Entrenamiento. Si Entrenamiento falla (error 500/timeout), la excepción
        // escala y corta el flujo, garantizando que el aviso NO quede marcado como aplicado.
        await trainingPlanServiceClient.ApplyAthleteSessionAdjustmentAsync(
            sessionId,
            remoteRequest,
            cancellationToken);

        // 4. Marcar como aplicado y persistir en la base de datos de Asistente
        alert.MarkAsApplied(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ApplyAlertAdjustmentResponseDto(
            Message: command.NextSession
                ? $"Ajuste preventivo aplicado a la próxima sesión ({sessionDate:dd/MM})."
                : "Ajuste preventivo aplicado exitosamente en Entrenamiento.",
            AlertId: alert.Id,
            Status: alert.Status.ToString(),
            SessionDate: sessionDate);
    }

    private const int NextSessionSearchDays = 7;

    private async Task<(Guid SessionId, DateOnly Date)> FindTodaySessionAsync(
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var todayRoutine = await trainingPlanServiceClient.GetTodayRoutineAsync(athleteId, cancellationToken);
        if (todayRoutine?.SessionId is not { } sessionId || sessionId == Guid.Empty)
            throw new DomainException("El atleta no tiene una sesión de entrenamiento programada para el día de hoy.");

        return (sessionId, ArgentinaTime.Today);
    }

    // Recorre los días siguientes (sin hoy) hasta encontrar una sesión; corta en una semana para no
    // ajustar algo demasiado lejano, cuando el aviso ya no refleja cómo está el atleta.
    private async Task<(Guid SessionId, DateOnly Date)> FindNextSessionAsync(
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var today = ArgentinaTime.Today;
        for (var offset = 1; offset <= NextSessionSearchDays; offset++)
        {
            var date = today.AddDays(offset);
            var routine = await trainingPlanServiceClient.GetRoutineForDateAsync(athleteId, date, cancellationToken);
            if (routine?.SessionId is { } sessionId && sessionId != Guid.Empty)
                return (sessionId, date);
        }

        throw new DomainException(
            $"El atleta no tiene sesiones de entrenamiento programadas en los próximos {NextSessionSearchDays} días.");
    }
}
