using Application.Common.PlanDrafts;
using Application.DTOs.PlanDrafts;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Domain.Entities;

namespace Application.UseCases.PlanDrafts.ApprovePlanDraft;

public sealed record ApprovePlanDraftCommand(Guid Id);

public sealed class ApprovePlanDraftCommandHandler(
    IPlanDraftRepository planDraftRepository,
    ITrainingPlanServiceClient trainingPlanServiceClient,
    IUnitOfWork unitOfWork)
{
    public const string CreatePlanFailedMessage = "No se pudo crear la planificación, probá de nuevo";
    public const string AssignmentFailedWarning = "Se creó la planificación pero no se pudo asignar";

    public async Task<ApprovePlanDraftResponseDto> HandleAsync(
        ApprovePlanDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var draft = await planDraftRepository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new NotFoundException("El borrador solicitado no existe.");

        if (draft.Status == PlanDraftStatus.Discarded)
            throw new ConflictException(draft.GetNonEditableStatusMessage());

        // Idempotencia: aprobar dos veces no crea dos planificaciones; devuelve el approved_plan_id existente
        if (draft.Status == PlanDraftStatus.Approved &&
            draft.ApprovedPlanId.HasValue &&
            draft.ApprovedPlanId.Value != Guid.Empty)
        {
            var existingPlanId = draft.ApprovedPlanId.Value;
            return new ApprovePlanDraftResponseDto(
                Id: draft.Id,
                PlanId: existingPlanId,
                ApprovedPlanId: existingPlanId,
                Status: draft.Status.ToString(),
                Assigned: true,
                AssignmentId: null,
                Warning: null,
                Warnings: []
            );
        }

        var savePlanRequest = PlanDraftMapper.ToSaveTrainingPlanRequest(draft);

        Guid createdPlanId;
        try
        {
            createdPlanId = await trainingPlanServiceClient.CreatePlanAsync(savePlanRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TrainingServiceUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TrainingServiceUnavailableException(CreatePlanFailedMessage, ex);
        }

        if (createdPlanId == Guid.Empty)
            throw new TrainingServiceUnavailableException(CreatePlanFailedMessage);

        var assignRequest = PlanDraftMapper.ToAssignTrainingPlanRequest(draft, createdPlanId);
        AssignTrainingPlanResult assignResult;
        try
        {
            assignResult = await trainingPlanServiceClient.TryAssignPlanAsync(assignRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            assignResult = new AssignTrainingPlanResult(Assigned: false, AssignmentId: null);
        }

        draft.Approve(createdPlanId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var warning = assignResult.Assigned ? null : AssignmentFailedWarning;
        IReadOnlyList<string> warnings = assignResult.Assigned ? [] : [AssignmentFailedWarning];

        return new ApprovePlanDraftResponseDto(
            Id: draft.Id,
            PlanId: createdPlanId,
            ApprovedPlanId: createdPlanId,
            Status: draft.Status.ToString(),
            Assigned: assignResult.Assigned,
            AssignmentId: assignResult.AssignmentId,
            Warning: warning,
            Warnings: warnings
        );
    }
}
