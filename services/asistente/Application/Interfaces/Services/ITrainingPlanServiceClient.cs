using Application.DTOs.DayAdjustments;
using Application.DTOs.DayConflicts;
using Application.DTOs.PlanDrafts;

namespace Application.Interfaces.Services;

public sealed record AssignTrainingPlanResult(
    bool Assigned,
    Guid? AssignmentId = null
);

/// <summary>
/// Cliente HTTP hacia el microservicio de Entrenamiento para crear y asignar
/// la planificación aprobada (#116 · KAN-411), consultar la rutina del día y
/// restricciones por dolor (#156 · KAN-451) y aplicar el ajuste del día (#157 · KAN-452).
/// </summary>
public interface ITrainingPlanServiceClient
{
    Task<Guid> CreatePlanAsync(
        SaveTrainingPlanRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AssignTrainingPlanResult> TryAssignPlanAsync(
        AssignTrainingPlanRequestDto request,
        CancellationToken cancellationToken = default);

    Task<TodayRoutineRemoteDto?> GetTodayRoutineAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExerciseRestrictionCheckResultRemoteDto>> CheckExerciseRestrictionsAsync(
        string painZones,
        IReadOnlyList<Guid> exerciseIds,
        CancellationToken cancellationToken = default);

    Task ApplyAthleteSessionAdjustmentAsync(
        Guid sessionId,
        ApplyAthleteAdjustmentRequestRemoteDto request,
        CancellationToken cancellationToken = default);
}
