using Application.DTOs.PlanDrafts;

namespace Application.Interfaces.Services;

public sealed record AssignTrainingPlanResult(
    bool Assigned,
    Guid? AssignmentId = null
);

/// <summary>
/// Cliente HTTP hacia el microservicio de Entrenamiento para crear y asignar
/// la planificación aprobada a partir de un borrador de IA (#116 · KAN-411).
/// </summary>
public interface ITrainingPlanServiceClient
{
    Task<Guid> CreatePlanAsync(
        SaveTrainingPlanRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AssignTrainingPlanResult> TryAssignPlanAsync(
        AssignTrainingPlanRequestDto request,
        CancellationToken cancellationToken = default);
}
